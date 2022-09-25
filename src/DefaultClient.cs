using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Dnvm
{
	internal class DefaultClient : IClient
	{
		static HttpClient s_noRedirectClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false });
		static HttpClient s_defaultClient = new HttpClient();
		public async Task DownloadArchiveAndExtractAsync(Uri uri, string extractPath)
		{
			using (var rawStream = await s_defaultClient.GetStreamAsync(uri))
			{
				if (!uri.Segments[^1].EndsWith(".zip"))
					ExtractTarGz(rawStream, extractPath);
				else
					ExtractZip(rawStream, extractPath);
			}
		}

		public static void ExtractTarGz(Stream stream, string outputDir)
		{
			// A GZipStream is not seekable, so copy it first to a MemoryStream
			using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
			{
				ExtractTar(gzip, outputDir);
			}
		}

		public static string GetNullTerminatedString(ReadOnlySpan<byte> buffer)
		{
			int i = 0;
			for (; i < buffer.Length; i++)
			{
				if (buffer[i] == 0)
					break;
			}
			return Encoding.ASCII.GetString(buffer[..i]);
		}

		public static long GetSize(ReadOnlySpan<byte> buffer)
		{
			long acc = 0;
			long place = 1;
			for (int i = 10; i >= 0; i--)
			{
				acc += place * (buffer[i] & 0x07);
				place *= 8;
			}
			return acc;
		}

		// Tar manual: https://www.gnu.org/software/tar/manual/html_node/Standard.html
		public static void ExtractTar(Stream stream, string outputDir)
		{
			int bufSize = 1024 * 1024;
			var buffer = new byte[bufSize];
			Span<byte> header = new(buffer, 0, 512);
			long pos = 0;
			while (true)
			{
				stream.ReadExactly(header);
				pos += 512;
				if (header[0] == 0)
					break;

				// Name is a null terminated ascii string in the first 100 bytes of the tar
				var name = GetNullTerminatedString(header[..100]);

				// Size is a 12 byte null terminated string that represents the ascii representation of the size in base 8
				var size = GetSize(header[124..136]);

				var output = Path.Combine(outputDir, name);
				if (!Directory.Exists(Path.GetDirectoryName(output)))
					Directory.CreateDirectory(Path.GetDirectoryName(output));
				// Trying to write directories that exist is problematic
				if (!Path.EndsInDirectorySeparator(name))
				{
					using (var str = File.Open(output, FileMode.OpenOrCreate, FileAccess.Write))
					{
						long tot = 0;
						do
						{
							int r = stream.Read(buffer, 0, size - tot < bufSize ? (int)(size - tot) : bufSize);
							str.Write(buffer, 0, r);
							tot += r;
						} while (tot < size);
						pos += tot;
					}
				}

				// Next file header is aligned on 512 bytes
				int offset = (int)(512 - (pos % 512));
				if (offset == 512)
					offset = 0;

				stream.ReadExactly(buffer[..offset]);
				pos += offset;
				if (pos % 512 != 0)
					throw new NotImplementedException();
			}
		}

		static void ExtractZip(Stream stream, string extractPath)
		{
			ZipArchive za = new(stream);
			za.ExtractToDirectory(extractPath);
			return;
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
