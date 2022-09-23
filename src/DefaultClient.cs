using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Dnvm
{
	internal class DefaultClient : IClient
	{
		static HttpClient s_noRedirectClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false });
		static HttpClient s_defaultClient = new HttpClient();
		public async Task DownloadArchiveAndExtractAsync(Uri uri, string extractPath)
		{
			var rawStream = await s_defaultClient.GetStreamAsync(uri);
			if (!uri.Segments[^1].EndsWith(".zip"))
				await ExtractTarGz(rawStream, extractPath);
			else
				await ExtractZip(rawStream, extractPath);
		}
		static async Task ExtractTarGz(Stream stream, string extractPath)
		{
			List<Task> extractions = new();
			using (GZipInputStream gZipInputStream = new(stream))
			using (TarReader tarReader = new(gZipInputStream))
			{
				while (await tarReader.GetNextEntryAsync(true) is TarEntry next)
				{
					string filePath = Path.GetFullPath(Path.Combine(extractPath, next.Name));
					if (Path.EndsInDirectorySeparator(filePath))
					{
						Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
						continue;
					}

					extractions.Add(next.ExtractToFileAsync(filePath, true));
				}
				await Task.WhenAll(extractions);
			}
			return;

		}

		static async Task ExtractZip(Stream stream, string extractPath)
		{

			//var r = SharpCompress.Readers.Zip.ZipReader.Open(stream);
			//Directory.CreateDirectory(extractPath);
			//r.WriteAllToDirectory(extractPath, new SharpCompress.Common.ExtractionOptions() { Overwrite = true });
			//return;

			//using (System.IO.Compression.ZipArchive za = new(stream))
			//{
			//	za.ExtractToDirectory(extractPath, true);
			//}
			//return;

			using (ZipInputStream zipInputStream = new ZipInputStream(stream))
			{
				int bufferSize = 1024 * 1024;// 81920;
				byte[] buffer = new byte[bufferSize];
				while (zipInputStream.GetNextEntry() is ZipEntry entry)
				{
					if (!entry.IsFile)
						continue;
					string filePath = Path.GetFullPath(Path.Combine(extractPath, entry.Name));
					Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
					long fileSize = entry.Size;
					int totalBytesRead = 0;
					using (FileStream extractedFile = File.Create(filePath, bufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous))
						do
						{
							int bytesLeft = (int)(fileSize - totalBytesRead);
							int bytesToRead = bytesLeft > bufferSize ? bufferSize : bytesLeft;
							int bytesBuffered = await zipInputStream.ReadAsync(buffer, 0, bytesToRead);
							totalBytesRead += bytesBuffered;
							extractedFile.Write(buffer, 0, bytesBuffered);
						} while (totalBytesRead < fileSize);
				}
				return;
			}
		}

		public async Task<HttpResponseMessage> GetHeadersAsync(Uri uri, bool followRedirects = false)
		{
			HttpResponseMessage? response = null;
			int redirects = followRedirects ? 10 : 1;
			for (int i = 0; i < redirects; i++)
			{
				var requestMessage = new HttpRequestMessage(
					HttpMethod.Head,
					uri);
				response = await s_noRedirectClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
				if (response.StatusCode != HttpStatusCode.MovedPermanently)
					break;
				uri = response.Headers.Location!;
			}
			return response!;
		}

		public Task<string> GetStringAsync(Uri uri)
			=> s_defaultClient.GetStringAsync(uri);
	}
}
