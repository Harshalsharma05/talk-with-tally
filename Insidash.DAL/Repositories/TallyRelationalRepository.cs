using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Insidash.DAL.Context;
using Insidash.DAL.Entities;
using Newtonsoft.Json;

namespace Insidash.DAL.Repositories
{
    public class TallyRelationalRepository : ITallyRelationalRepository
    {
        public void SaveLedgers(int companyId, List<TallyLedger> ledgers)
        {
            using (var ctx = new InsidashTallyContext())
            {
                var connection = (SqlConnection)ctx.Database.Connection;
                if (connection.State != ConnectionState.Open)
                    connection.Open();

                using (var tx = connection.BeginTransaction())
                {
                    try
                    {
                        // Delete old ledgers for this company
                        using (var cmd = new SqlCommand("DELETE FROM TallyLedger WHERE CompanyID = @cid", connection, tx))
                        {
                            cmd.Parameters.AddWithValue("@cid", companyId);
                            cmd.ExecuteNonQuery();
                        }

                        // Bulk Insert new ledgers
                        var table = new DataTable();
                        table.Columns.Add("LedgerID", typeof(string));
                        table.Columns.Add("CompanyID", typeof(int));
                        table.Columns.Add("Name", typeof(string));
                        table.Columns.Add("Parent", typeof(string));
                        table.Columns.Add("ClosingBalance", typeof(decimal));
                        table.Columns.Add("SyncedAt", typeof(DateTime));

                        foreach (var ledger in ledgers)
                        {
                            table.Rows.Add(
                                Guid.NewGuid().ToString(),
                                companyId,
                                ledger.Name ?? string.Empty,
                                ledger.Parent ?? string.Empty,
                                ledger.ClosingBalance,
                                DateTime.Now
                            );
                        }

                        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, tx))
                        {
                            bulk.DestinationTableName = "TallyLedger";
                            bulk.BatchSize = 500;
                            bulk.WriteToServer(table);
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        public void SaveVouchers(int companyId, List<TallyVoucher> vouchers)
        {
            using (var ctx = new InsidashTallyContext())
            {
                var connection = (SqlConnection)ctx.Database.Connection;
                if (connection.State != ConnectionState.Open)
                    connection.Open();

                using (var tx = connection.BeginTransaction())
                {
                    try
                    {
                        // 1. Create temporary staging table for Vouchers
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
                CREATE TABLE #TallyVoucherStaging (
                    VoucherID  NVARCHAR(50),
                    CompanyID  INT,
                    Date       DATE,
                    VchType    NVARCHAR(100),
                    PartyName  NVARCHAR(255),
                    Amount     DECIMAL(18,2),
                    Narration  NVARCHAR(MAX),
                    SyncedAt   DATETIME
                )";
                            cmd.ExecuteNonQuery();
                        }

                        // 2. Populate Voucher Staging Table
                        var voucherTable = new DataTable();
                        voucherTable.Columns.Add("VoucherID", typeof(string));
                        voucherTable.Columns.Add("CompanyID", typeof(int));
                        voucherTable.Columns.Add("Date", typeof(DateTime));
                        voucherTable.Columns.Add("VchType", typeof(string));
                        voucherTable.Columns.Add("PartyName", typeof(string));
                        voucherTable.Columns.Add("Amount", typeof(decimal));
                        voucherTable.Columns.Add("Narration", typeof(string));
                        voucherTable.Columns.Add("SyncedAt", typeof(DateTime));

                        // Collections to safely extract and flatten nested paths
                        var parsedInventoryLines = new List<TallyVoucherInventoryItem>();
                        var parsedLedgerLines = new List<TallyVoucherLedgerItem>();

                        foreach (var voucher in vouchers)
                        {
                            string finalVoucherId = string.IsNullOrWhiteSpace(voucher.VoucherID)
                                ? Guid.NewGuid().ToString()
                                : voucher.VoucherID;

                            voucherTable.Rows.Add(
                                finalVoucherId,
                                companyId,
                                voucher.Date,
                                voucher.VchType ?? string.Empty,
                                voucher.PartyName ?? string.Empty,
                                voucher.Amount,
                                voucher.Narration ?? string.Empty,
                                DateTime.Now
                            );

                            // Gather child inventory lines
                            if (voucher.InventoryItems != null && voucher.InventoryItems.Any())
                            {
                                foreach (var item in voucher.InventoryItems)
                                {
                                    item.VoucherID = finalVoucherId;
                                    item.CompanyID = companyId;
                                    parsedInventoryLines.Add(item);
                                }
                            }

                            // Gather child ledger lines
                            if (voucher.LedgerEntries != null && voucher.LedgerEntries.Any())
                            {
                                foreach (var item in voucher.LedgerEntries)
                                {
                                    item.VoucherID = finalVoucherId;
                                    item.CompanyID = companyId;
                                    parsedLedgerLines.Add(item);
                                }
                            }
                        }

                        // 3. Write parent items up to staging
                        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, tx))
                        {
                            bulk.DestinationTableName = "#TallyVoucherStaging";
                            bulk.BatchSize = 500;
                            bulk.WriteToServer(voucherTable);
                        }

                        // 4. Run MERGE operation to process voucher headers
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
                MERGE TallyVoucher AS target
                USING #TallyVoucherStaging AS source
                    ON target.VoucherID = source.VoucherID
                        AND target.CompanyID = source.CompanyID
                WHEN MATCHED THEN
                    UPDATE SET
                        target.Date      = source.Date,
                        target.VchType   = source.VchType,
                        target.PartyName = source.PartyName,
                        target.Amount    = source.Amount,
                        target.Narration = source.Narration,
                        target.SyncedAt  = source.SyncedAt
                WHEN NOT MATCHED THEN
                    INSERT (VoucherID, CompanyID, Date, VchType, PartyName, Amount, Narration, SyncedAt)
                    VALUES (source.VoucherID, source.CompanyID, source.Date,
                            source.VchType, source.PartyName, source.Amount,
                            source.Narration, source.SyncedAt);

                DROP TABLE #TallyVoucherStaging;";
                            cmd.ExecuteNonQuery();
                        }

                        // 5. Clean historical inventory detail lines
                        if (vouchers.Any())
                        {
                            using (var cmd = connection.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                var uniqueVoucherIds = vouchers.Where(v => !string.IsNullOrWhiteSpace(v.VoucherID))
                                                               .Select(v => $"'{v.VoucherID.Replace("'", "''")}'")
                                                               .Distinct();

                                if (uniqueVoucherIds.Any())
                                {
                                    string idList = string.Join(",", uniqueVoucherIds);
                                    cmd.CommandText = $"DELETE FROM TallyVoucherInventoryItem WHERE CompanyID = @cid AND VoucherID IN ({idList})";
                                    cmd.Parameters.AddWithValue("@cid", companyId);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }

                        // 6. Bulk copy child inventory details to destination table if any lines exist
                        if (parsedInventoryLines.Any())
                        {
                            var inventoryTable = new DataTable();
                            inventoryTable.Columns.Add("VoucherID", typeof(string));
                            inventoryTable.Columns.Add("CompanyID", typeof(int));
                            inventoryTable.Columns.Add("StockItemName", typeof(string));
                            inventoryTable.Columns.Add("Quantity", typeof(decimal));
                            inventoryTable.Columns.Add("Rate", typeof(decimal));
                            inventoryTable.Columns.Add("Amount", typeof(decimal));
                            inventoryTable.Columns.Add("SyncedAt", typeof(DateTime));

                            foreach (var item in parsedInventoryLines)
                            {
                                inventoryTable.Rows.Add(
                                    item.VoucherID,
                                    companyId,
                                    item.StockItemName ?? string.Empty,
                                    item.Quantity,
                                    item.Rate,
                                    item.Amount,
                                    DateTime.Now
                                );
                            }

                            using (var bulkItems = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, tx))
                            {
                                bulkItems.DestinationTableName = "TallyVoucherInventoryItem";
                                bulkItems.BatchSize = 500;

                                bulkItems.ColumnMappings.Add("VoucherID", "VoucherID");
                                bulkItems.ColumnMappings.Add("CompanyID", "CompanyID");
                                bulkItems.ColumnMappings.Add("StockItemName", "StockItemName");
                                bulkItems.ColumnMappings.Add("Quantity", "Quantity");
                                bulkItems.ColumnMappings.Add("Rate", "Rate");
                                bulkItems.ColumnMappings.Add("Amount", "Amount");
                                bulkItems.ColumnMappings.Add("SyncedAt", "SyncedAt");

                                bulkItems.WriteToServer(inventoryTable);
                            }
                        } // ── Inventory block ends cleanly here ──

                        // 7. Clean historical ledger detail lines to prevent duplicates
                        if (vouchers.Any())
                        {
                            using (var cmd = connection.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                var uniqueVoucherIds = vouchers.Where(v => !string.IsNullOrWhiteSpace(v.VoucherID))
                                                               .Select(v => $"'{v.VoucherID.Replace("'", "''")}'")
                                                               .Distinct();

                                if (uniqueVoucherIds.Any())
                                {
                                    string idList = string.Join(",", uniqueVoucherIds);
                                    cmd.CommandText = $"DELETE FROM TallyVoucherLedgerItem WHERE CompanyID = @cid AND VoucherID IN ({idList})";
                                    cmd.Parameters.AddWithValue("@cid", companyId);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }

                        // 8. Bulk copy child ledger details to destination table if any lines exist
                        if (parsedLedgerLines.Any())
                        {
                            var ledgerTable = new DataTable();
                            ledgerTable.Columns.Add("VoucherID", typeof(string));
                            ledgerTable.Columns.Add("CompanyID", typeof(int));
                            ledgerTable.Columns.Add("LedgerName", typeof(string));
                            ledgerTable.Columns.Add("Amount", typeof(decimal));
                            ledgerTable.Columns.Add("IsDeemedPositive", typeof(bool));
                            ledgerTable.Columns.Add("SyncedAt", typeof(DateTime));

                            foreach (var item in parsedLedgerLines)
                            {
                                ledgerTable.Rows.Add(
                                    item.VoucherID,
                                    companyId,
                                    item.LedgerName ?? string.Empty,
                                    item.Amount,
                                    item.IsDeemedPositive,
                                    DateTime.Now
                                );
                            }

                            using (var bulkLedgers = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, tx))
                            {
                                bulkLedgers.DestinationTableName = "TallyVoucherLedgerItem";
                                bulkLedgers.BatchSize = 500;

                                bulkLedgers.ColumnMappings.Add("VoucherID", "VoucherID");
                                bulkLedgers.ColumnMappings.Add("CompanyID", "CompanyID");
                                bulkLedgers.ColumnMappings.Add("LedgerName", "LedgerName");
                                bulkLedgers.ColumnMappings.Add("Amount", "Amount");
                                bulkLedgers.ColumnMappings.Add("IsDeemedPositive", "IsDeemedPositive");
                                bulkLedgers.ColumnMappings.Add("SyncedAt", "SyncedAt");

                                bulkLedgers.WriteToServer(ledgerTable);
                            }
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }
        public string ExecuteQueryToDynamicJson(string sqlQuery)
        {
            using (var ctx = new InsidashTallyContext("name=InsidashTallyDbReadOnly"))
            {
                var connection = ctx.Database.Connection;
                if (connection.State != ConnectionState.Open)
                    connection.Open();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = sqlQuery;
                    cmd.CommandTimeout = 15;
                    using (var reader = cmd.ExecuteReader())
                    {
                        var dataTable = new DataTable();
                        dataTable.Load(reader);
                        return JsonConvert.SerializeObject(dataTable);
                    }
                }
            }
        }

        public void SaveStockItems(int companyId, List<TallyStockItem> items)
        {
            using (var ctx = new InsidashTallyContext())
            {
                var connection = (SqlConnection)ctx.Database.Connection;
                if (connection.State != ConnectionState.Open)
                    connection.Open();

                using (var tx = connection.BeginTransaction())
                {
                    try
                    {
                        // Delete old stock items for this company
                        using (var cmd = new SqlCommand("DELETE FROM TallyStockItem WHERE CompanyID = @cid", connection, tx))
                        {
                            cmd.Parameters.AddWithValue("@cid", companyId);
                            cmd.ExecuteNonQuery();
                        }

                        // Bulk Insert new stock items
                        var table = new DataTable();
                        table.Columns.Add("StockItemID", typeof(string));
                        table.Columns.Add("CompanyID", typeof(int));
                        table.Columns.Add("Name", typeof(string));
                        table.Columns.Add("Parent", typeof(string));
                        table.Columns.Add("Unit", typeof(string));
                        table.Columns.Add("ClosingQty", typeof(decimal));
                        table.Columns.Add("ClosingValue", typeof(decimal));
                        table.Columns.Add("SyncedAt", typeof(DateTime));

                        foreach (var item in items)
                        {
                            table.Rows.Add(
                                Guid.NewGuid().ToString(),
                                companyId,
                                item.Name ?? string.Empty,
                                item.Parent ?? string.Empty,
                                item.Unit ?? string.Empty,
                                item.ClosingQty,
                                item.ClosingValue,
                                DateTime.Now
                            );
                        }

                        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, tx))
                        {
                            bulk.DestinationTableName = "TallyStockItem";
                            bulk.BatchSize = 500;
                            bulk.WriteToServer(table);
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        public void SaveBillOutstandings(int companyId, List<TallyBillOutstanding> bills)
        {
            using (var ctx = new InsidashTallyContext())
            {
                var connection = (SqlConnection)ctx.Database.Connection;
                if (connection.State != ConnectionState.Open)
                    connection.Open();

                using (var tx = connection.BeginTransaction())
                {
                    try
                    {
                        // Delete old outstanding bills for this company
                        using (var cmd = new SqlCommand("DELETE FROM TallyBillOutstanding WHERE CompanyID = @cid", connection, tx))
                        {
                            cmd.Parameters.AddWithValue("@cid", companyId);
                            cmd.ExecuteNonQuery();
                        }

                        // Bulk Insert new outstanding bills
                        var table = new DataTable();
                        table.Columns.Add("BillID", typeof(string));
                        table.Columns.Add("CompanyID", typeof(int));
                        table.Columns.Add("PartyName", typeof(string));
                        table.Columns.Add("BillDate", typeof(DateTime));
                        table.Columns.Add("BillRef", typeof(string));
                        table.Columns.Add("Amount", typeof(decimal));
                        table.Columns.Add("DueDate", typeof(object)); // Allow DBNull for DueDate
                        table.Columns.Add("SyncedAt", typeof(DateTime));

                        foreach (var bill in bills)
                        {
                            table.Rows.Add(
                                Guid.NewGuid().ToString(),
                                companyId,
                                bill.PartyName ?? string.Empty,
                                bill.BillDate,
                                bill.BillRef ?? string.Empty,
                                bill.Amount,
                                (object)bill.DueDate ?? DBNull.Value,
                                DateTime.Now
                            );
                        }

                        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, tx))
                        {
                            bulk.DestinationTableName = "TallyBillOutstanding";
                            bulk.BatchSize = 500;
                            bulk.WriteToServer(table);
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        // ── ADD THIS METHOD inside TallyRelationalRepository class ──
        public void SaveGroups(int companyId, List<TallyGroup> groups)
        {
            using (var ctx = new InsidashTallyContext())
            {
                var connection = (SqlConnection)ctx.Database.Connection;
                if (connection.State != ConnectionState.Open)
                    connection.Open();

                using (var tx = connection.BeginTransaction())
                {
                    try
                    {
                        // 1. Clear existing groups for this company to prevent duplicates
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "DELETE FROM TallyGroup WHERE CompanyID = @cid";
                            cmd.Parameters.AddWithValue("@cid", companyId);
                            cmd.ExecuteNonQuery();
                        }

                        // 2. Bulk copy new groups
                        if (groups.Any())
                        {
                            var table = new DataTable();
                            table.Columns.Add("CompanyID", typeof(int));
                            table.Columns.Add("Name", typeof(string));
                            table.Columns.Add("Parent", typeof(string));
                            table.Columns.Add("SyncedAt", typeof(DateTime));

                            foreach (var g in groups)
                            {
                                table.Rows.Add(
                                    companyId,
                                    g.Name ?? string.Empty,
                                    g.Parent ?? string.Empty,
                                    DateTime.Now
                                );
                            }

                            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, tx))
                            {
                                bulk.DestinationTableName = "TallyGroup";
                                bulk.BatchSize = 500;

                                bulk.ColumnMappings.Add("CompanyID", "CompanyID");
                                bulk.ColumnMappings.Add("Name", "Name");
                                bulk.ColumnMappings.Add("Parent", "Parent");
                                bulk.ColumnMappings.Add("SyncedAt", "SyncedAt");

                                bulk.WriteToServer(table);
                            }
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}
