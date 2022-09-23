using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;

namespace Dnvm
{
	internal class DefaultClient : IClient
	{
		static HttpClient s_noRedirectClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false });
		static HttpClient s_defaultClient = new HttpClient();
		public async Task DownloadArchiveAndExtractAsync(Uri uri, string archivePath, string extractPath)
		{
			var rawStream = await s_defaultClient.GetStreamAsync(uri);
			if (!uri.Segments[^1].EndsWith(".zip"))
			{
				List<Task> extractions = new();
				using (GZipInputStream gZipInputStream = new(rawStream))
				using (TarReader tarReader = new(gZipInputStream))
					while (tarReader.GetNextEntry() is TarEntry next)
					{
						extractions.Add(next.ExtractToFileAsync(Path.Combine(extractPath, next.Name), true));
					}
				await Task.WhenAll(extractions);
				return;
			}

			using (var ms = new MemoryStream())
			{
				await rawStream.CopyToAsync(ms);
				ms.Seek(0, SeekOrigin.Begin);

				int bufferSize = 1024 * 64;
				byte[] buffer = new byte[bufferSize];
				using (ZipFile zf = new(ms))
				{
					foreach (ZipEntry entry in zf)
					{
						if (!entry.IsFile)
							continue;
						string filePath = Path.Combine(extractPath, entry.Name);
						Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
						using (var outputFile = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.Write))
						using (var zipEntry = zf.GetInputStream(entry))
						{
							int bytesRead = 0;
							do
							{
								bytesRead = await zipEntry.ReadAsync(buffer, 0, bufferSize);
								outputFile.Write(buffer, 0, bytesRead);
							} while (bytesRead > 0);
						}
					}
				}
			}


			//try
			//{
			//	using (var archiveHttpStream = await s_noRedirectClient.GetStreamAsync(uri))
			//	using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough))
			//	{
			//		await archiveHttpStream.CopyToAsync(tempArchiveFile);
			//		await tempArchiveFile.FlushAsync();
			//		tempArchiveFile.Close();
			//	}

			//	await ExtractArchiveToDir(archivePath, extractPath);
			//}
			//finally
			//{

			//	File.Delete(archivePath);
			//}
		}
		//static async Task ExtractArchiveToDir(string archivePath, string dirPath)
		//{
		//	Directory.CreateDirectory(dirPath);
		//	if (!(Utilities.CurrentOS == OSPlatform.Windows))
		//	{
		//		var psi = new ProcessStartInfo()
		//		{
		//			FileName = "tar",
		//			ArgumentList = { "-xzf", $"{archivePath}", "-C", $"{dirPath}" },
		//		};

		//		var p = Process.Start(psi);
		//		if (p is not null)
		//		{
		//			await p.WaitForExitAsync();
		//			if (p.ExitCode != 0)
		//				throw new DnvmException("Failed to extract the downloaded archive");
		//		}
		//		throw new DnvmException("Failed to extract the downloaded archive");
		//	}
		//	else
		//	{
		//		ZipFile.ExtractToDirectory(archivePath, dirPath, overwriteFiles: true);
		//	}
		//}

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
