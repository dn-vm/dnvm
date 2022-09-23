using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Dnvm.Tests
{
	internal class MockHttpClient : IClient
	{
		public Task<HttpResponseMessage> GetHeadersAsync(Uri uri, bool followRedirects = false)
		{
			throw new NotImplementedException();
		}

		public Task DownloadArchiveAndExtractAsync(Uri uri, string extractPath)
		{
			throw new NotImplementedException();
		}

		public Task<string> GetStringAsync(Uri uri)
		{
			throw new NotImplementedException();
		}
	}
}
