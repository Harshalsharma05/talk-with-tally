using System;

namespace Insidash.DAL.Entities
{
    // Matches dbo.TallyCompanyConfig (ConfigID NVARCHAR(50), CompanyID INT, SyncToken NVARCHAR(255), IsActive BIT, CreatedOn DATETIME)
    public class TallyCompanyConfig
    {
        public string ConfigID { get; set; }
        public int CompanyID { get; set; }
        public string SyncToken { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}
