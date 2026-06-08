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
                    Name = (string)ledger.Attribute("NAME"),
                    Parent = (string)ledger.Element("PARENT"),
                    ClosingBalance = (string)ledger.Element("CLOSINGBALANCE")
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
                    Date = (string)voucher.Element("DATE"),
                    VchType = (string)voucher.Element("VOUCHERTYPENAME"),
                    PartyName = (string)voucher.Element("PARTYNAME"),
                    Amount = (string)voucher.Element("AMOUNT"),
                    Narration = (string)voucher.Element("NARRATION")
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
                    Name = (string)l.Attribute("NAME") ?? string.Empty,
                    Parent = (string)l.Element("PARENT") ?? string.Empty,
                    ClosingBalance = ParseClosingBalance((string)l.Element("CLOSINGBALANCE"))
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
                    VoucherID = (string)v.Element("GUID") ?? Guid.NewGuid().ToString(),
                    Date = DateTime.TryParse((string)v.Element("DATE"), out DateTime d) ? d : DateTime.Today,
                    VchType = (string)v.Element("VOUCHERTYPENAME") ?? string.Empty,
                    PartyName = (string)v.Element("PARTYNAME") ?? string.Empty,
                    Amount = ParseAmount((string)v.Element("AMOUNT")),
                    Narration = (string)v.Element("NARRATION") ?? string.Empty
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
                .Select(s => new TallyStockItemDto
                {
                    Name = (string)s.Attribute("NAME") ?? (string)s.Element("NAME") ?? string.Empty,
                    Parent = (string)s.Element("PARENT") ?? string.Empty,
                    Unit = (string)s.Element("BASEUNITS") ?? string.Empty,
                    ClosingQty = ParseDecimalSafe((string)s.Element("CLOSINGBALANCE")),
                    ClosingValue = ParseDecimalSafe((string)s.Element("CLOSINGVALUE"))
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
                string partyName = (string)ledger.Attribute("NAME") ?? (string)ledger.Element("NAME") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(partyName)) continue;

                foreach (var bill in ledger.Descendants("BILLDETAILS.LIST"))
                {
                    string billDateStr = (string)bill.Element("BILLDATE") ?? string.Empty;
                    DateTime billDate = DateTime.TryParseExact(billDateStr, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime bd)
                        ? bd
                        : (DateTime.TryParse(billDateStr, out DateTime d) ? d : DateTime.Today);

                    string billRef = (string)bill.Element("BILLREF") ?? string.Empty;
                    
                    // Amount can be in BILLCLVAL or AMOUNT
                    string rawAmt = (string)bill.Element("BILLCLVAL") ?? (string)bill.Element("AMOUNT") ?? "0";
                    decimal amount = ParseAmount(rawAmt);

                    string rawDueDate = (string)bill.Element("BILLDATEDUE") ?? (string)bill.Element("BILLDUEFROM") ?? string.Empty;
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