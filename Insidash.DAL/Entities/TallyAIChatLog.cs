using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Insidash.DAL.Entities
{
    [Table("TallyAIChatLog")] // Maps strictly to a dedicated Tally log table
    public class TallyAIChatLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long LogID { get; set; }

        public int CompanyID { get; set; }
        public string UserQuestion { get; set; }
        public string AIResponse { get; set; }
        public string ResponseType { get; set; }
        public string SQLQuery { get; set; }
        public int? ExecutionTime { get; set; }
        public bool? IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime? CreatedDate { get; set; }
    }
}