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
                .Select(v => new TallyVoucherDto
                {
                    VoucherID = (string)v.Element("GUID") ?? (string)v.Element("Guid") ?? Guid.NewGuid().ToString(),
                    Date = DateTime.TryParse((string)v.Element("DATE") ?? (string)v.Element("Date"), out DateTime d) ? d : DateTime.Today,
                    VchType = (string)v.Element("VOUCHERTYPENAME") ?? (string)v.Element("VoucherTypeName") ?? string.Empty,
                    PartyName = (string)v.Element("PARTYNAME") ?? (string)v.Element("PartyName") ?? string.Empty,
                    Amount = ParseAmount((string)v.Element("AMOUNT") ?? (string)v.Element("Amount")),
                    Narration = (string)v.Element("NARRATION") ?? (string)v.Element("Narration") ?? string.Empty
                })
                .ToList();
        }
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
                .Select(s => {
                    // Try uppercase first, then TitleCase
                    string parent = (string)s.Element("PARENT") ?? (string)s.Element("Parent") ?? string.Empty;
                    string unit = (string)s.Element("BASEUNITS") ?? (string)s.Element("BaseUnits") ?? string.Empty;
                    string rawQty = (string)s.Element("CLOSINGBALANCE") ?? (string)s.Element("ClosingBalance");
                    string rawValue = (string)s.Element("CLOSINGVALUE") ?? (string)s.Element("ClosingValue");

                    return new TallyStockItemDto
                    {
                        Name = (string)s.Attribute("NAME") ?? (string)s.Attribute("Name") ?? (string)s.Element("NAME") ?? (string)s.Element("Name") ?? string.Empty,
                        Parent = parent,
                        Unit = unit,
                        ClosingQty = ParseDecimalSafe(rawQty),
                        ClosingValue = ParseDecimalSafe(rawValue)
                    };
                })
                .ToList();
        }

        public List<TallyBillOutstandingDto> ParseBillOutstandingsToDto(string rawXml)
        {
            string cleanXml = SanitizeXml(rawXml);
            XDocument document = XDocument.Parse(cleanXml);
            var list = new List<TallyBillOutstandingDto>();

            foreach (var ledger in document.Descendants("LEDGER"))
            {
                string partyName = (string)ledger.Attribute("NAME") ?? (string)ledger.Attribute("Name") ?? (string)ledger.Element("NAME") ?? (string)ledger.Element("Name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(partyName)) continue;

                // Match both uppercase and TitleCase for BILLDETAILS.LIST / BillDetails.List
                var billElements = ledger.Descendants("BILLDETAILS.LIST").Concat(ledger.Descendants("BillDetails.List"));

                foreach (var bill in billElements)
                {
                    string billDateStr = (string)bill.Element("BILLDATE") ?? (string)bill.Element("BillDate") ?? string.Empty;
                    DateTime billDate = DateTime.TryParseExact(billDateStr, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime bd)
                        ? bd
                        : (DateTime.TryParse(billDateStr, out DateTime d) ? d : DateTime.Today);

                    string billRef = (string)bill.Element("BILLREF") ?? (string)bill.Element("BillRef") ?? string.Empty;
                    
                    // Amount can be in BILLCLVAL, AMOUNT, or TitleCase variations
                    string rawAmt = (string)bill.Element("BILLCLVAL") 
                                 ?? (string)bill.Element("BillClVal") 
                                 ?? (string)bill.Element("AMOUNT") 
                                 ?? (string)bill.Element("Amount") 
                                 ?? "0";
                    decimal amount = ParseAmount(rawAmt);

                    string rawDueDate = (string)bill.Element("BILLDATEDUE") 
                                     ?? (string)bill.Element("BillDateDue") 
                                     ?? (string)bill.Element("BILLDUEFROM") 
                                     ?? (string)bill.Element("BillDueFrom") 
                                     ?? string.Empty;
                    DateTime? dueDate = null;
                    if (!string.IsNullOrWhiteSpace(rawDueDate))
                    {
                        dueDate = DateTime.TryParseExact(rawDueDate, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dd)
                            ? dd
                            : (DateTime.TryParse(rawDueDate, out DateTime d2) ? d2 : (DateTime?)null);
                    }

                    list.Add(new TallyBillOutstandingDto
                    {
                        PartyName = partyName,
                        BillDate = billDate,
                        BillRef = billRef,
                        Amount = amount,
                        DueDate = dueDate
                    });
                }
            }

            return list;
        }
    }
}