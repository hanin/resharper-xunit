using Xunit;
using Xunit.Extensions;

namespace Foo
{
    public class Tests
    {
        [Theory]
        [InlineData("foo")]
        public void TestMethod(string data)
        {
        }
    }
}
