using System.Collections.Generic;
using Insidash.DAL.Entities;

namespace Insidash.DAL.Repositories
{
    public interface ITallyRelationalRepository
    {
        void SaveLedgers(int companyId, List<TallyLedger> ledgers);
        void SaveVouchers(int companyId, List<TallyVoucher> vouchers);
        void SaveStockItems(int companyId, List<TallyStockItem> items);
        void SaveBillOutstandings(int companyId, List<TallyBillOutstanding> bills);
        string ExecuteQueryToDynamicJson(string sqlQuery);
    }
}
