using System;

namespace Insidash.DAL.Entities
{
    // Matches dbo.TallySnapshot (SnapshotID NVARCHAR(50), CompanyID INT, DataType NVARCHAR(100), JsonContent NVARCHAR(MAX), RawXml NVARCHAR(MAX), SyncedAt DATETIME, RecordCount INT)
    public class TallySnapshot
    {
        public string SnapshotID { get; set; }
        public int CompanyID { get; set; }
        public string DataType { get; set; }
        public string JsonContent { get; set; }
        public string RawXml { get; set; }
        public DateTime SyncedAt { get; set; }
        public int RecordCount { get; set; }
    }
}
