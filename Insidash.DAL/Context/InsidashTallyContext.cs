using System.Data.Entity;
using Insidash.DAL.Entities;

namespace Insidash.DAL.Context
{
    // EF6 DbContext for Insidash DAL
    public class InsidashTallyContext : DbContext
    {
        // Uses connection string name InsidashTallyDb from Web.config/App.config
        public InsidashTallyContext()
            : base("name=InsidashTallyDb")
        {
        }

        public InsidashTallyContext(string nameOrConnectionString)
            : base(nameOrConnectionString)
        {
        }

        public DbSet<TallyCompanyConfig> TallyCompanyConfigs { get; set; }
        public DbSet<TallySnapshot> TallySnapshots { get; set; }
        public DbSet<AIChatLog> AIChatLogs { get; set; }
        public DbSet<TallyAIChatLog> TallyAIChatLogs { get; set; } // NEW: Dedicated Tally Chat Log DbSet
        public DbSet<TallyLedger> TallyLedgers { get; set; }
        public DbSet<TallyVoucher> TallyVouchers { get; set; }
        public DbSet<TallySyncState> TallySyncStates { get; set; }
        public DbSet<TallyQueryTemplate> TallyQueryTemplates { get; set; }
        public DbSet<TallyStockItem> TallyStockItems { get; set; }
        public DbSet<TallyBillOutstanding> TallyBillOutstandings { get; set; }
        public DbSet<TallyActivationKey> TallyActivationKeys { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            // Explicitly declare primary keys to match existing DB schema
            modelBuilder.Entity<TallyCompanyConfig>().ToTable("TallyCompanyConfig");
            modelBuilder.Entity<TallyCompanyConfig>().HasKey(c => c.ConfigID);

            modelBuilder.Entity<TallySnapshot>().ToTable("TallySnapshot");
            modelBuilder.Entity<TallySnapshot>().HasKey(s => s.SnapshotID);

            modelBuilder.Entity<AIChatLog>().ToTable("AIChatLog");
            modelBuilder.Entity<AIChatLog>().HasKey(l => l.LogID);

            // NEW: Table and Primary Key mapping for TallyAIChatLog
            modelBuilder.Entity<TallyAIChatLog>().ToTable("TallyAIChatLog");
            modelBuilder.Entity<TallyAIChatLog>().HasKey(l => l.LogID);

            modelBuilder.Entity<TallyLedger>().ToTable("TallyLedger");
            modelBuilder.Entity<TallyLedger>().HasKey(l => l.LedgerID);

            modelBuilder.Entity<TallyVoucher>().ToTable("TallyVoucher");
            modelBuilder.Entity<TallyVoucher>().HasKey(v => v.VoucherID);

            modelBuilder.Entity<TallySyncState>().ToTable("TallySyncState");
            modelBuilder.Entity<TallySyncState>().HasKey(s => s.SyncStateID);

            modelBuilder.Entity<TallyQueryTemplate>().ToTable("TallyQueryTemplate");
            modelBuilder.Entity<TallyQueryTemplate>().HasKey(t => t.TemplateID);

            modelBuilder.Entity<TallyStockItem>().ToTable("TallyStockItem");
            modelBuilder.Entity<TallyStockItem>().HasKey(s => s.StockItemID);

            modelBuilder.Entity<TallyBillOutstanding>().ToTable("TallyBillOutstanding");
            modelBuilder.Entity<TallyBillOutstanding>().HasKey(b => b.BillID);

            modelBuilder.Entity<TallyActivationKey>().ToTable("TallyActivationKey");
            modelBuilder.Entity<TallyActivationKey>().HasKey(k => k.KeyID);

            base.OnModelCreating(modelBuilder);
        }
    }
}