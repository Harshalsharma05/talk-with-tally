using System;

namespace Insidash.DAL.Entities
{
    public class TallyVoucher
    {
        public string VoucherID { get; set; }
        public int CompanyID { get; set; }
        public DateTime Date { get; set; }
        public string VchType { get; set; }
        public string PartyName { get; set; }
        public decimal Amount { get; set; }
        public string Narration { get; set; }
        public DateTime SyncedAt { get; set; }
    }
}
