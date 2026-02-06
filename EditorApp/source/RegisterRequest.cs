using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.source
{
    public class RegisterRequest
    {
        public string user_name { get; set; } = ""; 
        public string invite_code { get; set; } = "";
    }

    public class RegisterResponse
    {
        public string user_name { get; set; } = ""; 
        public string api_key { get; set; } = "";
    }
}
