using Insidash.DAL.Entities;
using System.Collections.Generic;

namespace Insidash.DAL.Repositories
{
    public interface ITallySnapshotRepository
    {
        TallySnapshot GetLatest(int companyId, string dataType);
        void UpsertSnapshot(TallySnapshot snapshot);
    }
}
