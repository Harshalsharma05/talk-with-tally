using System;

namespace Insidash.DAL.Entities
{
    public class TallyGroup
    {
        public int GroupID { get; set; }
        public int CompanyID { get; set; }
        public string Name { get; set; }
        public string Parent { get; set; }
        public DateTime SyncedAt { get; set; }
    }
}