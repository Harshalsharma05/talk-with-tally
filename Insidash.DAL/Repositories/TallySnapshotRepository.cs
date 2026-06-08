using System;
using System.Linq;
using Insidash.DAL.Context;
using Insidash.DAL.Entities;

namespace Insidash.DAL.Repositories
{
    public class TallySnapshotRepository : ITallySnapshotRepository
    {
        public TallySnapshot GetLatest(int companyId, string dataType)
        {
            using (var ctx = new InsidashTallyContext())
            {
                return ctx.TallySnapshots
                    .Where(s => s.CompanyID == companyId && s.DataType == dataType)
                    .OrderByDescending(s => s.SyncedAt)
                    .FirstOrDefault();
            }
        }

        public void UpsertSnapshot(TallySnapshot incoming)
        {
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            using (var ctx = new InsidashTallyContext())
            {
                var existing = ctx.TallySnapshots
                    .FirstOrDefault(s => s.CompanyID == incoming.CompanyID && s.DataType == incoming.DataType);

                if (existing != null)
                {
                    existing.JsonContent = incoming.JsonContent;
                    existing.RawXml = incoming.RawXml;
                    existing.SyncedAt = DateTime.Now;
                    existing.RecordCount = incoming.RecordCount;
                }
                else
                {
                    incoming.SnapshotID = Guid.NewGuid().ToString();
                    incoming.SyncedAt = DateTime.Now;
                    ctx.TallySnapshots.Add(incoming);
                }

                ctx.SaveChanges();
            }
        }
    }
}
