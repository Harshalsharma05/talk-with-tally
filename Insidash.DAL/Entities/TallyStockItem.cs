using System;

namespace Insidash.DAL.Entities
{
    public class TallyStockItem
    {
        public string StockItemID { get; set; }
        public int CompanyID { get; set; }
        public string Name { get; set; }
        public string Parent { get; set; }
        public string Unit { get; set; }
        public decimal ClosingQty { get; set; }
        public decimal ClosingValue { get; set; }
        public DateTime SyncedAt { get; set; }
    }
}
