using System;
using System.Linq;
using System.Collections.Generic;
using Insidash.DAL.Context;
using Insidash.DAL.Entities;

namespace Insidash.DAL.Repositories
{
    public class AIChatLogRepository : IAIChatLogRepository
    {
        public void InsertLog(TallyAIChatLog log)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));

            using (var ctx = new InsidashTallyContext())
            {
                // Writes strictly to the dedicated TallyAIChatLog table
                ctx.TallyAIChatLogs.Add(log);
                ctx.SaveChanges();
            }
        }

        public IEnumerable<TallyAIChatLog> GetByCompany(int companyId, int page = 1, int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            using (var ctx = new InsidashTallyContext())
            {
                // Reads strictly from the dedicated TallyAIChatLog table
                return ctx.TallyAIChatLogs
                    .Where(l => l.CompanyID == companyId)
                    .OrderByDescending(l => l.CreatedDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            }
        }
    }
}