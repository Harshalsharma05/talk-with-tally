using System;

namespace Insidash.DAL.Entities
{
    public class TallyBillOutstanding
    {
        public string BillID { get; set; }
        public int CompanyID { get; set; }
        public string PartyName { get; set; }
        public DateTime BillDate { get; set; }
        public string BillRef { get; set; }
        public decimal Amount { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime SyncedAt { get; set; }
    }
}
