using System;

namespace Insidash.DAL.Entities
{
    public class TallyLedger
    {
        public string LedgerID { get; set; }
        public int CompanyID { get; set; }
        public string Name { get; set; }
        public string Parent { get; set; }
        public decimal ClosingBalance { get; set; }
        public DateTime SyncedAt { get; set; }
    }
}
