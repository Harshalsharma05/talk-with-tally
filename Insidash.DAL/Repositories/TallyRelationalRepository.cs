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
                        // Create staging table in the transaction
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

                        // Bulk Insert into staging table
                        var table = new DataTable();
                        table.Columns.Add("VoucherID", typeof(string));
                        table.Columns.Add("CompanyID", typeof(int));
                        table.Columns.Add("Date", typeof(DateTime));
                        table.Columns.Add("VchType", typeof(string));
                        table.Columns.Add("PartyName", typeof(string));
                        table.Columns.Add("Amount", typeof(decimal));
                        table.Columns.Add("Narration", typeof(string));
                        table.Columns.Add("SyncedAt", typeof(DateTime));

                        foreach (var voucher in vouchers)
                        {
                            table.Rows.Add(
                                string.IsNullOrWhiteSpace(voucher.VoucherID) ? Guid.NewGuid().ToString() : voucher.VoucherID,
                                companyId,
                                voucher.Date,
                                voucher.VchType ?? string.Empty,
                                voucher.PartyName ?? string.Empty,
                                voucher.Amount,
                                voucher.Narration ?? string.Empty,
                                DateTime.Now
                            );
                        }

                        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, tx))
                        {
                            bulk.DestinationTableName = "#TallyVoucherStaging";
                            bulk.BatchSize = 500;
                            bulk.WriteToServer(table);
                        }

                        // MERGE staging table into destination table
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
    }
}
