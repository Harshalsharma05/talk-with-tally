using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Insidash.BLL.Services
{
    public class Nl2SqlService
    {
        private readonly IAIService _aiService;

        public Nl2SqlService(IAIService aiService)
        {
            _aiService = aiService;
        }

        public async Task<string> GenerateSqlAsync(string userQuestion, int companyId)
        {
            string systemPrompt = $@"You are a translation assistant that converts natural language questions into raw MS SQL Server queries.
We have a SQL Server database 'Popway_BillingERP' with the following tables:

1. Table 'TallyLedger':
   - LedgerID (NVARCHAR(50), PRIMARY KEY)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - Name (NVARCHAR(255)) - Name of ledger account
   - Parent (NVARCHAR(255)) - Parent group name (e.g. Bank Accounts, Indirect Expenses)
   - ClosingBalance (DECIMAL(18,2)) - Negative indicates Debit (Dr), Positive indicates Credit (Cr)

2. Table 'TallyVoucher':
   - VoucherID (NVARCHAR(50), PRIMARY KEY)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - Date (DATE) - Transaction date
   - VchType (NVARCHAR(100)) - Voucher type (e.g. Sales, Payment, Receipt)
   - PartyName (NVARCHAR(255)) - Associated ledger/party name
   - Amount (DECIMAL(18,2)) - Transaction Amount
   - Narration (NVARCHAR(MAX)) - Remarks

3. Table 'Customer':
   - CustomerID (INT, PRIMARY KEY)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - DisplayName (NVARCHAR(255))
   - CompanyName (NVARCHAR(255))
   - CreatedOn (DATETIME) - Date when customer was created/registered
   - IsDelete (BIT) - Filter by IsDelete = 0 to exclude deleted customers

4. Table 'Invoice':
   - InvoiceID (INT, PRIMARY KEY)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - CustomerID (INT)
   - Date2 (DATE) - Invoice date
   - InvNo (NVARCHAR(255)) - Invoice number
   - CreatedOn (DATETIME) - Date when invoice was created
   - Final_Amt (FLOAT) - Invoice final amount
   - IsActive (BIT)
   - IsDelete (BIT) - Filter by IsDelete = 0

5. Table 'Payment':
   - PaymentID (INT, PRIMARY KEY)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - CustomerID (INT)
   - PayDate (DATETIME) - Date of payment transaction
   - PayAmount (FLOAT) - Payment amount
   - CreatedOn (DATETIME)
   - IsDelete (BIT) - Filter by IsDelete = 0

6. Table 'PaymentMap':
   - PaymentMapID (INT, PRIMARY KEY)
   - PaymentID (INT)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - InvoiceID (INT)
   - MapAmt (FLOAT) - Amount applied to the invoice
   - IsDelete (BIT) - Filter by IsDelete = 0

7. Table 'TallyStockItem':
   - StockItemID (NVARCHAR(50), PRIMARY KEY)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - Name (NVARCHAR(255)) - Product/Stock item name
   - Parent (NVARCHAR(255)) - Stock group name
   - Unit (NVARCHAR(50)) - Unit of measure (e.g. Nos, Pcs, Kgs)
   - ClosingQty (DECIMAL(18,4)) - Current stock quantity
   - ClosingValue (DECIMAL(18,2)) - Valuation of closing quantity

8. Table 'TallyBillOutstanding':
   - BillID (NVARCHAR(50), PRIMARY KEY)
   - CompanyID (INT) - Filter queries by CompanyID = {companyId}
   - PartyName (NVARCHAR(255)) - Customer/Sundry Debtor name
   - BillDate (DATE) - Outstanding bill date
   - BillRef (NVARCHAR(100)) - Bill reference number
   - Amount (DECIMAL(18,2)) - Outstanding amount
   - DueDate (DATE) - Bill payment due date

RULES:
- Respond ONLY with the executable SQL Query inside a markdown code block: ```sql <sql query here> ```. Do not add explanations.
- ALWAYS filter the queries by CompanyID = {companyId}.
- Be careful with the balance signs for TallyLedger: Debit balances (negative) vs. Credit balances (positive).
- Keep queries simple, optimized, and read-only. Only use SELECT statements.
- Stock/Inventory queries -> TallyStockItem (use ClosingQty for quantities and ClosingValue for valuation).
- Outstanding bills, aging, or receivables queries -> TallyBillOutstanding.
- Outstanding bills older than N days -> DATEDIFF(DAY, BillDate, GETDATE()) > N.

=====================
- Customer table has NO CreatedDate, NO AddedDate, NO JoinDate column. The creation date column is named 'CreatedOn'.
- Invoice table has Date2 (for invoice date) and CreatedOn (for creation date). Do not use InvoiceDate or CreatedDate.
- Payment table has PayDate (for transaction date) and CreatedOn (for creation date). Do not use CreatedDate for payment transaction date.
- NEVER use a column name not explicitly listed in the TABLES section above.
- If a query cannot be answered because a column does not exist or is unsupported, return: {{ ""sql"": ""UNSUPPORTED"" }}

SUBQUERY RULES:
- NEVER reference a table alias from the outer query inside a subquery.
- In subqueries, always re-JOIN the required table.
- Prefer LEFT JOIN in the main query over subqueries wherever possible.
- If you need Invoice data and Payment data together, use LEFT JOIN PaymentMap ON PaymentMap.InvoiceID = Invoice.InvoiceID directly — do not nest.";

            var result = await _aiService.ChatAsync(systemPrompt, userQuestion);
            if (!result.Success)
            {
                throw new Exception("LLM SQL Generation failed: " + result.ErrorMessage);
            }

            return ExtractSqlFromJson(result.Content);
        }

        private string StripMarkdownFences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Trim();
            if (text.StartsWith("```"))
            {
                int firstNewline = text.IndexOf('\n');
                if (firstNewline >= 0)
                    text = text.Substring(firstNewline + 1);
                if (text.EndsWith("```"))
                    text = text.Substring(0, text.Length - 3);
            }
            return text.Trim();
        }

        public bool IsSafeSelect(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;

            string upper = sql.Trim().ToUpperInvariant();

            // Whitelist safe stored procedures EXEC sp_Tally*
            if (upper.StartsWith("EXEC SP_TALLY"))
            {
                var whitelistedProcs = new[] { "EXEC SP_TALLYPROFITANDLOSS", "EXEC SP_TALLYBALANCESHEET", "EXEC SP_TALLYRECEIVABLESAGING" };
                if (whitelistedProcs.Any(p => upper.StartsWith(p)))
                {
                    // Ensure it doesn't contain any semi-colon or subsequent SQL commands to prevent SQL injection
                    if (upper.Contains(";") || upper.Contains("UNION") || upper.Contains("SELECT") || upper.Contains("INSERT") || upper.Contains("UPDATE") || upper.Contains("DELETE") || upper.Contains("DROP"))
                        return false;

                    return true;
                }
                return false;
            }

            // Must start with SELECT
            if (!upper.StartsWith("SELECT")) return false;

            // Must not contain any write/DDL keywords
            var forbidden = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE",
                                     "ALTER", "CREATE", "EXEC", "EXECUTE", "SP_",
                                     "XP_", "OPENROWSET", "OPENDATASOURCE" };
            foreach (var word in forbidden)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(upper, $@"\b{word}\b"))
                    return false;
            }

            // Must include CompanyID filter (prevents cross-tenant data leaks)
            if (!upper.Contains("COMPANYID")) return false;

            return true;
        }

        private string ExtractSqlFromJson(string jsonText)
        {
            jsonText = StripMarkdownFences(jsonText);
            try
            {
                // Parse potential JSON wrapper envelope (e.g. {"sql": "SELECT..."})
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(jsonText);
                if (parsed != null && parsed.TryGetValue("sql", out string sql))
                {
                    // Handle sentinel values the LLM returns for impossible queries
                    if (sql == "NO_DATE_COLUMN" || sql == "NO_DATA" || sql == "UNSUPPORTED")
                        throw new InvalidOperationException("AI_NoSQL: " + jsonText);

                    return sql;
                }
            }
            catch (Exception ex) when (!ex.Message.Contains("AI_NoSQL"))
            {
                // Fall through to raw check on catch
            }

            // Last resort: if the LLM returned raw SQL
            if (jsonText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return jsonText.Trim();

            throw new InvalidOperationException("AI_NoSQL: " + jsonText);
        }

        public async Task<string> FormulateResponseAsync(string userQuestion, string sqlQuery, string sqlResultJson)
        {
            string systemPrompt = @"You are TalkWithTally, an AI accounting assistant.
Answer the user's question by formatting the SQL query results into a clear, helpful, and natural financial response.
Always format currency values in INR (₹). Highlight details and summarize findings if multiple records are returned.";

            string userMessage = $@"User Question: {userQuestion}
Generated SQL Query: {sqlQuery}
Query Results (JSON): {sqlResultJson}";

            var result = await _aiService.ChatAsync(systemPrompt, userMessage);
            return result.Success ? result.Content : "Failed to generate financial answer: " + result.ErrorMessage;
        }
    }
}
