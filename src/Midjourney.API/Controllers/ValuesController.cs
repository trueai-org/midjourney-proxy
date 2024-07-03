using Microsoft.AspNetCore.Mvc;

namespace Midjourney.API.Controllers
{
    [Route("api/values")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        [HttpPost]
        public void Post()
        {
            var str = Request.GetRequestBody();
        }
    }
}