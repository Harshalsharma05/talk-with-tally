using System;
using System.Collections.Generic;
using System.Linq;
using Insidash.DAL.Context;

namespace Insidash.DAL.Repositories
{
    public class IntentRouterService
    {
        /// <summary>
        /// Returns a matched template SQL if a pre-written query covers the intent.
        /// Returns null if the question should go to the LLM (NL2SQL path).
        /// </summary>
        public string TryMatchTemplate(string userQuestion, out string templateName)
        {
            templateName = null;
            if (string.IsNullOrWhiteSpace(userQuestion)) return null;

            string lower = userQuestion.ToLowerInvariant().Trim();

            List<QueryTemplateDto> templates;
            using (var db = new InsidashTallyContext())
            {
                templates = db.TallyQueryTemplates
                    .Where(t => t.IsActive)
                    .Select(t => new QueryTemplateDto
                    {
                        TemplateID = t.TemplateID,
                        Name = t.Name,
                        Keywords = t.Keywords,
                        SqlQuery = t.SqlQuery,
                        IsActive = t.IsActive
                    })
                    .ToList();
            }

            foreach (var template in templates)
            {
                if (string.IsNullOrWhiteSpace(template.Keywords)) continue;

                var keywords = template.Keywords
                    .Split(',')
                    .Select(k => k.Trim().ToLowerInvariant())
                    .Where(k => k.Length > 0);

                if (keywords.Any(k => lower.Contains(k)))
                {
                    templateName = template.Name;
                    return template.SqlQuery;
                }
            }

            return null; // no match
        }
    }

    public class QueryTemplateDto
    {
        public int TemplateID { get; set; }
        public string Name { get; set; }
        public string Keywords { get; set; }
        public string SqlQuery { get; set; }
        public bool IsActive { get; set; }
    }
}
