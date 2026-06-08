using System;

namespace Insidash.DAL.Entities
{
    public class TallyQueryTemplate
    {
        public int TemplateID { get; set; }
        public string Name { get; set; }
        public string Keywords { get; set; }
        public string SqlQuery { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
