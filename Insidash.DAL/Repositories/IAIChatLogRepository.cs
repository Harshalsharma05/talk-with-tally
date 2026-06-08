using Insidash.DAL.Entities;
using System.Collections.Generic;

namespace Insidash.DAL.Repositories
{
    public interface IAIChatLogRepository
    {
        void InsertLog(AIChatLog log);
        IEnumerable<AIChatLog> GetByCompany(int companyId, int page = 1, int pageSize = 20);
    }
}
