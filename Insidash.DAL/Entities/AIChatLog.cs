using System;

namespace Insidash.DAL.Entities
{
    // Matches existing dbo.AIChatLog in Popway_BillingERP
    public class AIChatLog
    {
        // DB has LogID as bigint primary key
        public long LogID { get; set; }

        public int CompanyID { get; set; }
        public string UserQuestion { get; set; }
        public string AIResponse { get; set; }
        public string ResponseType { get; set; }
        public string SQLQuery { get; set; }
        // ExecutionTime stored as int in DB (nullable)
        public int? ExecutionTime { get; set; }
        // IsSuccess is nullable bit in the DB
        public bool? IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime? CreatedDate { get; set; }
    }
}
