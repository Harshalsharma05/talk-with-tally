using System;

namespace Insidash.DAL.Entities
{
    public class TallySyncState
    {
        public int SyncStateID { get; set; }
        public int CompanyID { get; set; }
        public string DataType { get; set; }
        public DateTime LastSyncedAt { get; set; }
        public string LastSyncStatus { get; set; }
        public int RecordsSynced { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
