using System;

namespace Insidash.DAL.Entities
{
    public class TallyVoucherLedgerItem
    {
        public int VoucherLedgerItemID { get; set; }
        public string VoucherID { get; set; }
        public int CompanyID { get; set; }
        public string LedgerName { get; set; }
        public decimal Amount { get; set; }
        public bool IsDeemedPositive { get; set; }
        public DateTime SyncedAt { get; set; }
    }
}