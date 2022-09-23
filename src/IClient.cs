using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Dnvm
{
	internal interface IClient
	{
		public Task<HttpResponseMessage> GetHeadersAsync(Uri uri, bool followRedirects = false);
		public Task DownloadArchiveAndExtractAsync(Uri uri, string archivePath, string extractPath);
		public Task<string> GetStringAsync(Uri uri);
	}
}
