using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Insidash.DAL.Entities
{
    [Table("TallyActivationKey")]
    public class TallyActivationKey
    {
        [Key]
        public string KeyID { get; set; }
        public int CompanyID { get; set; }
        public string ActivationKey { get; set; }
        public string SyncToken { get; set; }
        public bool IsActivated { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public string MachineID { get; set; }
        public string AgentVersion { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsActive { get; set; }
    }
}
