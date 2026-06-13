using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Insidash.DAL.Entities
{
    [Table("TallyVoucherInventoryItem")]
    public class TallyVoucherInventoryItem
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int InventoryLineItemID { get; set; }

        [Required]
        [StringLength(50)]
        public string VoucherID { get; set; }

        [Required]
        public int CompanyID { get; set; }

        [Required]
        [StringLength(255)]
        public string StockItemName { get; set; }

        public decimal Quantity { get; set; }
        
        public decimal Rate { get; set; }
        
        public decimal Amount { get; set; }
        
        public DateTime SyncedAt { get; set; }

        // Navigation property linking the item line back to its parent voucher header
        [ForeignKey("VoucherID")]
        public virtual TallyVoucher Voucher { get; set; }
    }
}