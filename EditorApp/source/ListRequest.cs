using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.source
{
    public class ListRequest
    {
        public string text { get; set; } = "";
        public string language { get; set; } = "";
        public string templateId { get; set; } = "";
    }

    public class ListResponse
    {
        public string result { get; set; } = "";
    }
}
