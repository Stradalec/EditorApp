using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.services
{
    public class CheckDifferenceService
    {
        private readonly IDiffer _differ;

        public CheckDifferenceService()
        {
            _differ = new Differ();
        }

        public DiffPaneModel GetLineDiffs(string original, string updated)
        {
            var inlineBuilder = new InlineDiffBuilder(_differ);
            return inlineBuilder.BuildDiffModel(original ?? "", updated ?? "");
        }
    }
}
