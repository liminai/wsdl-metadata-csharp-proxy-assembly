using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace WSDLToCodeConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            string url = "http://www.webxml.com.cn/WebServices/ValidateCodeWebService.asmx";
            try
            {
                bool result1 = WSDLHelper.CreateCSharpByWSDLI(new Uri(url + "?WSDL"), AppDomain.CurrentDomain.BaseDirectory + $"\\Code_{DateTime.Now.ToString("yyyymmddhhmmss")}.cs");
                Thread.Sleep(2000);
                bool result2 = WSDLHelper.CreateCSharpBySDI(new Uri(url + "?WSDL"), AppDomain.CurrentDomain.BaseDirectory + $"\\Code_{DateTime.Now.ToString("yyyymmddhhmmss")}.cs");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
