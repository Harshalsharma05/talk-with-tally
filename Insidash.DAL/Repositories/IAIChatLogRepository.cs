using System.Collections.Generic;
using Insidash.DAL.Entities;

namespace Insidash.DAL.Repositories
{
    public interface IAIChatLogRepository
    {
        void InsertLog(TallyAIChatLog log);
        IEnumerable<TallyAIChatLog> GetByCompany(int companyId, int page = 1, int pageSize = 20);
    }
}