using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace Insidash.BLL.Parsers
{
    public partial class TallyXmlParser
    {
        private static string SanitizeXml(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return xml;

            // Remove invalid character entities like &#4; or &#x04;
            xml = Regex.Replace(xml, @"&#x?([0-9a-fA-F]+);", match =>
            {
                string val = match.Groups[1].Value;
                try
                {
                    int codePoint = match.Value.StartsWith("&#x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToInt32(val, 16)
                        : Convert.ToInt32(val, 10);

                    // Check if it's a valid XML character
                    if (codePoint == 0x9 || codePoint == 0xA || codePoint == 0xD ||
                        (codePoint >= 0x20 && codePoint <= 0xD7FF) ||
                        (codePoint >= 0xE000 && codePoint <= 0xFFFD) ||
                        (codePoint >= 0x10000 && codePoint <= 0x10FFFF))
                    {
                        return match.Value; // keep it
                    }
                    return string.Empty; // strip it
                }
                catch
                {
                    return string.Empty; // strip on error
                }
            });

            // Strip invalid literal characters
            var buffer = new StringBuilder(xml.Length);
            foreach (char c in xml)
            {
                if (c == 0x9 || c == 0xA || c == 0xD ||
                    (c >= 0x20 && c <= 0xD7FF) ||
                    (c >= 0xE000 && c <= 0xFFFD) ||
                    char.IsSurrogate(c))
                {
                    buffer.Append(c);
                }
            }
            return buffer.ToString();
        }

        public decimal ParseClosingBalance(string rawVal)
        {
            if (string.IsNullOrWhiteSpace(rawVal)) return 0;
            string text = rawVal.Trim();

            bool isCredit = text.EndsWith("Cr", StringComparison.OrdinalIgnoreCase);
            bool isDebit = text.EndsWith("Dr", StringComparison.OrdinalIgnoreCase);

            string numberPart = text;
            if (isCredit || isDebit)
            {
                numberPart = text.Substring(0, text.Length - 2).Trim();
            }

            // Remove commas so decimal.TryParse compiles in all cultures
            numberPart = numberPart.Replace(",", "");

            if (decimal.TryParse(numberPart, out decimal val))
            {
                // Credit = positive, Debit = negative
                return isDebit ? -val : val;
            }
            return 0;
        }

        public decimal ParseAmount(string rawVal)
        {
            if (string.IsNullOrWhiteSpace(rawVal)) return 0;

            // Remove commas from amount strings
            string text = rawVal.Trim().Replace(",", "");

            bool isNegative = text.StartsWith("-");
            if (isNegative) text = text.Substring(1).Trim();

            if (decimal.TryParse(text, out decimal val))
            {
                return isNegative ? -val : val;
            }
            return 0;
        }

        public ParsedTallyData ParseLedgers(string rawXml)
        {
            string cleanXml = SanitizeXml(rawXml);
            XDocument document = XDocument.Parse(cleanXml);
            List<object> ledgers = document.Descendants("LEDGER")
                .Select(ledger => new
                {
                    Name = (string)ledger.Attribute("NAME") ?? (string)ledger.Attribute("Name"),
                    Parent = (string)ledger.Element("PARENT") ?? (string)ledger.Element("Parent"),
                    ClosingBalance = (string)ledger.Element("CLOSINGBALANCE") ?? (string)ledger.Element("ClosingBalance")
                })
                .Cast<object>()
                .ToList();

            return new ParsedTallyData
            {
                DataType = "Ledgers",
                RecordCount = ledgers.Count,
                JsonContent = JsonConvert.SerializeObject(ledgers)
            };
        }

        public ParsedTallyData ParseVouchers(string rawXml)
        {
            string cleanXml = SanitizeXml(rawXml);
            XDocument document = XDocument.Parse(cleanXml);
            List<object> vouchers = document.Descendants("VOUCHER")
                .Select(voucher => new
                {
                    Date = (string)voucher.Element("DATE") ?? (string)voucher.Element("Date"),
                    VchType = (string)voucher.Element("VOUCHERTYPENAME") ?? (string)voucher.Element("VoucherTypeName"),
                    PartyName = (string)voucher.Element("PARTYNAME") ?? (string)voucher.Element("PartyName"),
                    Amount = (string)voucher.Element("AMOUNT") ?? (string)voucher.Element("Amount"),
                    Narration = (string)voucher.Element("NARRATION") ?? (string)voucher.Element("Narration")
                })
                .Cast<object>()
                .ToList();

            return new ParsedTallyData
            {
                DataType = "Vouchers",
                RecordCount = vouchers.Count,
                JsonContent = JsonConvert.SerializeObject(vouchers)
            };
        }

        public List<TallyGroupDto> ParseGroupsToDto(string rawXml)
        {
            string cleanXml = SanitizeXml(rawXml);
            XDocument document = XDocument.Parse(cleanXml);
            return document.Descendants("GROUP")
                .Concat(document.Descendants("Group"))
                .Select(g => {
                    string parent = (string)g.Element("PARENT") ?? (string)g.Element("Parent") ?? string.Empty;
                    
                    // Clean up Tally's hidden \x04 control character prefix if present
                    if (parent.StartsWith("\x04")) parent = parent.Substring(1).Trim();
                    parent = parent.Trim();

                    return new TallyGroupDto
                    {
                        Name = (string)g.Attribute("NAME") ?? (string)g.Attribute("Name") ?? (string)g.Element("NAME") ?? (string)g.Element("Name") ?? string.Empty,
                        Parent = parent
                    };
                })
                .ToList();
        }
        public List<TallyLedgerDto> ParseLedgersToDto(string rawXml)
        {
            string cleanXml = SanitizeXml(rawXml);
            XDocument document = XDocument.Parse(cleanXml);
            return document.Descendants("LEDGER")
                .Select(l => new TallyLedgerDto
                {
                    Name = (string)l.Attribute("NAME") ?? (string)l.Attribute("Name") ?? string.Empty,
                    Parent = (string)l.Element("PARENT") ?? (string)l.Element("Parent") ?? string.Empty,
                    ClosingBalance = ParseClosingBalance((string)l.Element("CLOSINGBALANCE") ?? (string)l.Element("ClosingBalance"))
                })
                .ToList();
        }

        public List<TallyVoucherDto> ParseVouchersToDto(string rawXml)
        {
            string cleanXml = SanitizeXml(rawXml);
            XDocument document = XDocument.Parse(cleanXml);
            return document.Descendants("VOUCHER")
                .Select(v =>
                {
                    // Precise Date Parsing
                    string rawDate = (string)v.Element("DATE") ?? (string)v.Element("Date") ?? string.Empty;
                    DateTime voucherDate = DateTime.TryParseExact(rawDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsedDate)
                        ? parsedDate
                        : (DateTime.TryParse(rawDate, out DateTime fallbackDate) ? fallbackDate : DateTime.Today);

                    string voucherId = (string)v.Element("GUID") ?? (string)v.Element("Guid") ?? Guid.NewGuid().ToString();

                    // ── 1. Extract nested ledger entries ──
                    var ledgerList = v.Descendants("ALLLEDGERENTRIES.LIST")
                        .Concat(v.Descendants("AllLedgerEntries.List"))
                        .Select(le => new TallyVoucherLedgerItemDto
                        {
                            LedgerName = (string)le.Element("LEDGERNAME") ?? (string)le.Element("LedgerName") ?? string.Empty,
                            Amount = ParseAmount((string)le.Element("AMOUNT") ?? (string)le.Element("Amount")),
                            IsDeemedPositive = ((string)le.Element("ISDEEMEDPOSITIVE") ?? (string)le.Element("IsDeemedPositive") ?? "").Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase)
                        })
                        .ToList();

                    // ── 2. Extract nested inventory allocations ──
                    var inventoryList = v.Descendants("INVENTORYALLOCATIONS.LIST")
                        .Concat(v.Descendants("InventoryAllocations.List"))
                        .Select(ia => new TallyVoucherInventoryItemDto
                        {
                            VoucherID = voucherId,
                            StockItemName = (string)ia.Element("STOCKITEMNAME") ?? (string)ia.Element("StockItemName") ?? string.Empty,
                            Quantity = ParseDecimalSafe((string)ia.Element("BILLEDQTY") ?? (string)ia.Element("BilledQty") ?? (string)ia.Element("ACTUALQTY") ?? (string)ia.Element("ActualQty")),
                            Rate = ParseDecimalSafe((string)ia.Element("RATE") ?? (string)ia.Element("Rate")),
                            Amount = ParseAmount((string)ia.Element("AMOUNT") ?? (string)ia.Element("Amount"))
                        })
                        .ToList();

                    return new TallyVoucherDto
                    {
                        VoucherID = voucherId,
                        Date = voucherDate,
                        VchType = (string)v.Element("VOUCHERTYPENAME") ?? (string)v.Element("VoucherTypeName") ?? string.Empty,

                        PartyName = (string)v.Element("PARTYLEDGERNAME")
                                 ?? (string)v.Element("PartyLedgerName")
                                 ?? (string)v.Element("PARTYNAME")
                                 ?? (string)v.Element("PartyName")
                                 ?? string.Empty,

                        Amount = ParseAmount((string)v.Element("AMOUNT") ?? (string)v.Element("Amount")),
                        Narration = (string)v.Element("NARRATION") ?? (string)v.Element("Narration") ?? string.Empty,

                        InventoryItems = inventoryList,
                        LedgerEntries = ledgerList
                    };
                })
                .ToList();
        }
        /// <summary>
        /// Auxiliary cleaner method to strip unit metrics (like 'Nos', 'Pcs', '/Kg') 
        /// out of raw Tally XML numbers before applying a decimal conversion.
        /// </summary>
        private decimal CleanAndParseTallyNumeric(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0;

            // Remove alphabetical characters and formatting spaces/slashes
            string cleanString = System.Text.RegularExpressions.Regex.Replace(input, @"[A-Za-z\/ ]", "");

            if (decimal.TryParse(cleanString, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }
            return 0;
        }
    }

    public class TallyGroupDto
    {
        public string Name { get; set; }
        public string Parent { get; set; }
    }

    public class TallyLedgerDto
    {
        public string Name { get; set; }
        public string Parent { get; set; }
        public decimal ClosingBalance { get; set; }
    }

    public class TallyVoucherDto
    {
        public string VoucherID { get; set; }
        public DateTime Date { get; set; }
        public string VchType { get; set; }
        public string PartyName { get; set; }
        public decimal Amount { get; set; }
        public string Narration { get; set; }
        public List<TallyVoucherLedgerItemDto> LedgerEntries { get; set; } = new List<TallyVoucherLedgerItemDto>();
        public List<TallyVoucherInventoryItemDto> InventoryItems { get; set; } = new List<TallyVoucherInventoryItemDto>();
    }

    // ── NEW: Holds child ledger allocations (Tax, Sales, Expenses etc.) ──
    public class TallyVoucherLedgerItemDto
    {
        public string LedgerName { get; set; }
        public decimal Amount { get; set; }
        public bool IsDeemedPositive { get; set; } // Yes = True (Debit), No = False (Credit)
    }

    public class TallyVoucherInventoryItemDto
    {
        public string VoucherID { get; set; }
        public string StockItemName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
    }

    public class TallyStockItemDto
    {
        public string Name { get; set; }
        public string Parent { get; set; }
        public string Unit { get; set; }
        public decimal ClosingQty { get; set; }
        public decimal ClosingValue { get; set; }
    }

    public class TallyBillOutstandingDto
    {
        public string PartyName { get; set; }
        public DateTime BillDate { get; set; }
        public string BillRef { get; set; }
        public decimal Amount { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public partial class TallyXmlParser
    {
        private decimal ParseDecimalSafe(string rawVal)
        {
            if (string.IsNullOrWhiteSpace(rawVal)) return 0;

            string text = rawVal.Trim().Replace(",", "");

            var match = Regex.Match(text, @"-?\d+(\.\d+)?");
            if (match.Success)
            {
                if (decimal.TryParse(match.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal val))
                {
                    bool isNegative = text.StartsWith("-") || text.EndsWith("Dr", StringComparison.OrdinalIgnoreCase);
                    return isNegative ? -Math.Abs(val) : Math.Abs(val);
                }
            }
            return 0;
        }

        public List<TallyStockItemDto> ParseStockItemsToDto(string rawXml)
        {
            string cleanXml = SanitizeXml(rawXml);
            XDocument document = XDocument.Parse(cleanXml);
            return document.Descendants("STOCKITEM")
                .Select(s =>
                {
                    // Try uppercase first, then TitleCase
                    string parent = (string)s.Element("PARENT") ?? (string)s.Element("Parent") ?? string.Empty;
                    if (parent.StartsWith("\x04")) parent = parent.Substring(1).Trim();
                    string unit = (string)s.Element("BASEUNITS") ?? (string)s.Element("BaseUnits") ?? string.Empty;
                    string rawQty = (string)s.Element("CLOSINGBALANCE") ?? (string)s.Element("ClosingBalance");
                    string rawValue = (string)s.Element("CLOSINGVALUE") ?? (string)s.Element("ClosingValue");

                    return new TallyStockItemDto
                    {
                        Name = (string)s.Attribute("NAME") ?? (string)s.Attribute("Name") ?? (string)s.Element("NAME") ?? (string)s.Element("Name") ?? string.Empty,
                        Parent = parent,
                        Unit = unit,
                        ClosingQty = ParseDecimalSafe(rawQty),
                        ClosingValue = Math.Abs(ParseDecimalSafe(rawValue)) // to ensure value is always positive, as it represents total value of stock on hand regardless of credit/debit nature
                    };
                })
                .ToList();
        }

        public List<TallyBillOutstandingDto> ParseBillOutstandingsToDto(string rawXml)
        {
            string cleanXml = SanitizeXml(rawXml);
            XDocument document = XDocument.Parse(cleanXml);
            var list = new List<TallyBillOutstandingDto>();

            // Support both uppercase <BILL> and TitleCase <Bill> tags
            var billElements = document.Descendants("BILL").Concat(document.Descendants("Bill"));

            foreach (var bill in billElements)
            {
                // PARENT contains the Customer/Debtor Ledger Name (e.g., "Rohan")
                string partyName = (string)bill.Element("PARENT") ?? (string)bill.Element("Parent") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(partyName)) continue;

                // Clean up any potential ASCII control characters from parent name
                if (partyName.StartsWith("\x04")) partyName = partyName.Substring(1).Trim();
                partyName = partyName.Trim();

                // NAME contains the Bill Reference Number (e.g., "INV-ROHAN-01")
                string billRef = (string)bill.Element("NAME") ?? (string)bill.Element("Name") ?? string.Empty;

                // BILLDATE contains the creation date of the outstanding bill (yyyyMMdd)
                string billDateStr = (string)bill.Element("BILLDATE") ?? (string)bill.Element("BillDate") ?? string.Empty;
                DateTime billDate = DateTime.TryParseExact(billDateStr, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime bd)
                    ? bd
                    : (DateTime.TryParse(billDateStr, out DateTime d) ? d : DateTime.Today);

                // BILLDATEDUE contains the due date (yyyyMMdd)
                string rawDueDate = (string)bill.Element("BILLDATEDUE") ?? (string)bill.Element("BillDateDue") ?? string.Empty;
                DateTime? dueDate = null;
                if (!string.IsNullOrWhiteSpace(rawDueDate))
                {
                    dueDate = DateTime.TryParseExact(rawDueDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dd)
                        ? dd
                        : (DateTime.TryParse(rawDueDate, out DateTime d2) ? d2 : (DateTime?)null);
                }

                // CLOSINGBALANCE contains the unpaid outstanding balance amount
                string rawAmt = (string)bill.Element("CLOSINGBALANCE") ?? (string)bill.Element("ClosingBalance") ?? "0";
                decimal amount = ParseAmount(rawAmt);

                list.Add(new TallyBillOutstandingDto
                {
                    PartyName = partyName,
                    BillDate = billDate,
                    BillRef = billRef,
                    Amount = amount,
                    DueDate = dueDate
                });
            }

            return list;
        }
    }
}