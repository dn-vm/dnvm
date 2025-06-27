using Azure.Identity;
using Dnvm.Signing;
using Serde;
using Serde.CmdLine;
using Spectre.Console;
using StaticCs;
using System.Text;

partial class Program
{
    [GenerateDeserialize]
    [Command("mk-keys")]
    partial record MkKeys
    {
        [CommandGroup("command")]
        public required MkKeysCommand Command { get; init; }
    }

    [Closed]
    [GenerateDeserialize]
    abstract partial record MkKeysCommand
    {
        private MkKeysCommand() { }

        [Command("get-root-key", Summary = "Fetch the root key from Azure Key Vault and print it in PEM format.")]
        public partial record GetRootKey : MkKeysCommand
        { }

        [Command("make-keys", Summary = "Generate a new signing key pair in PEM format. The private key is written to the specified output file. The public key is written to a file with the same name but with '.pub' appended.")]
        public partial record MakeKeys : MkKeysCommand
        {
            [CommandParameter(0, "output-file", Description = "The file to write the private key to.")]
            public required string OutputFile { get; init; }
        }

        [Command("sign-keys", Summary = "Sign a public key file using the root key. The signature is written to the output file.")]
        public partial record SignKeys : MkKeysCommand
        {
            [CommandParameter(0, "pub-key-file", Description = "The public key file to sign.")]
            public required string PubKeyFile { get; init; }
            [CommandParameter(1, "root-key-file", Description = "The root key file to use for signing.")]
            public required string RootKeyFile { get; init; }
        }

        [Command("verify-release-key", Summary = "Verify a signature against a public key using the root key.")]
        public partial record VerifyReleaseKey : MkKeysCommand
        {
            [CommandParameter(0, "root-key-file", Description = "The root key file.")]
            public required string RootKeyFile { get; init; }
            [CommandParameter(1, "pub-key-file", Description = "The public key file to verify.")]
            public required string PubKeyFile { get; init; }
        }

        [Command("sign-release", Summary = "Sign a release file using the private key from the release key.")]
        public partial record SignRelease : MkKeysCommand
        {
            [CommandParameter(0, "priv-key-file", Description = "The private key file.")]
            public required string PrivKeyFile { get; init; }
            [CommandParameter(1, "release-file", Description = "The release file to sign.")]
            public required string ReleaseFile { get; init; }
        }

        [Command("verify-release", Summary = "Verify a release file against the public key and root key.")]
        public partial record VerifyRelease : MkKeysCommand
        {
            [CommandParameter(0, "pub-key-file", Description = "The public key file.")]
            public required string PubKeyFile { get; init; }
            [CommandParameter(2, "release-file", Description = "The release file to verify.")]
            public required string ReleaseFile { get; init; }
        }
    }
    static async Task<int> Main(string[] args)
    {
        if (!CmdLine.TryParse<MkKeys>(args, AnsiConsole.Console, out var cmd))
        {
            return 1;
        }
        switch (cmd.Command)
        {
            case MkKeysCommand.GetRootKey:
                return await GetRootKey();
            case MkKeysCommand.MakeKeys { OutputFile: var outputFile }:
                return await MakeKeys(outputFile);
            case MkKeysCommand.SignKeys signKeys:
                return await SignReleaseKey(signKeys.PubKeyFile, signKeys.RootKeyFile);
            case MkKeysCommand.VerifyReleaseKey verifyReleaseKey:
                return await VerifyReleaseKey(verifyReleaseKey.RootKeyFile, verifyReleaseKey.PubKeyFile) ? 0 : 1;
            case MkKeysCommand.SignRelease signRelease:
                return await SignRelease(signRelease.PrivKeyFile, signRelease.ReleaseFile);
            case MkKeysCommand.VerifyRelease verifyRelease:
                return await VerifyRelease(verifyRelease.PubKeyFile, verifyRelease.ReleaseFile) ? 0 : 1;
        }
        return 1;
    }

    static async Task<int> GetRootKey()
    {
        try
        {
            var cred = new DefaultAzureCredential();
            Console.Error.WriteLine($"Trying to connect to Key Vault: {KeyMgr.RootKeyVaultUrl}");
            Console.Error.WriteLine($"Fetching key: {KeyMgr.RootKeyName}");
            var rootKey = await KeyMgr.FetchRootKeyFromAzure(cred);
            var pem = rootKey.ExportToPem();
            Console.Error.WriteLine("Root key PEM:");
            Console.WriteLine(pem);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching the key: {ex.Message}");

            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }

            Console.WriteLine("\nTroubleshooting steps:");
            Console.WriteLine("1. Install and log in to Azure CLI:");
            Console.WriteLine("   brew install azure-cli");
            Console.WriteLine("   az login");
            Console.WriteLine("\n2. Or set environment variables for service principal:");
            Console.WriteLine("   export AZURE_TENANT_ID=<your-tenant-id>");
            Console.WriteLine("   export AZURE_CLIENT_ID=<your-client-id>");
            Console.WriteLine("   export AZURE_CLIENT_SECRET=<your-client-secret>");
            Console.WriteLine("\n3. Make sure you have permissions to access the Key Vault");
            Console.WriteLine("4. Verify the Key Vault name and key name are correct");
        }
        return 1;
    }

    static async Task<int> MakeKeys(string outputFile)
    {
        // Generate a new signing key pair
        var (publicKey, privateKey) = KeyMgr.GenerateReleaseKey();
        await File.WriteAllTextAsync(outputFile, privateKey);
        Console.WriteLine($"Private key written to {outputFile}");
        await File.WriteAllTextAsync(outputFile + ".pub", publicKey);
        Console.WriteLine($"Public key written to {outputFile}.pub");
        return 0;
    }

    /// <summary>
    /// Signs a public key file using the root key from Azure Key Vault.
    /// The signature is written to the specified output file.
    /// </summary>
    static async Task<int> SignReleaseKey(string keyFile, string rootKeyFile)
    {
        var sigFile = keyFile + ".sig";

        // Verify the key in the root key file matches the Azure Key Vault root key
        var cred = new DefaultAzureCredential();
        using var azureRoot = await KeyMgr.FetchRootKeyFromAzure(cred);
        using var fileRoot = KeyMgr.ParsePublicRootKey(await File.ReadAllTextAsync(rootKeyFile));
        if (!fileRoot.Equals(azureRoot))
        {
            Console.WriteLine("Root key in file does not match Azure Key Vault root key.");
            return 1;
        }

        // Read the public key file as bytes
        byte[] pubKeyBytes = await File.ReadAllBytesAsync(keyFile);
        var sig = await KeyMgr.SignReleaseKey(cred, pubKeyBytes);

        // Write the signature to the output file
        await File.WriteAllBytesAsync(sigFile, sig);
        Console.WriteLine($"Signature written to {sigFile}");
        return 0;
    }

    static async Task<bool> VerifyReleaseKey(string rootKeyFile, string pubKeyFile)
    {
        var sigFile = pubKeyFile + ".sig";

        Console.WriteLine("Verifying release key signature...");
        Console.WriteLine();

        // Read the root key
        var rootKeyString = await File.ReadAllTextAsync(rootKeyFile);
        using var rootKey = KeyMgr.ParsePublicRootKey(rootKeyString);
        var pubKeyBytes = await File.ReadAllBytesAsync(pubKeyFile);
        var pubKeyString = Encoding.UTF8.GetString(pubKeyBytes);
        var sigBytes = await File.ReadAllBytesAsync(sigFile);

        Console.WriteLine("Public key: ");
        Console.WriteLine(pubKeyString);
        Console.WriteLine();
        Console.WriteLine("Signature bytes: ");
        Console.WriteLine(Convert.ToBase64String(sigBytes));
        Console.WriteLine();

        // Verify the signature
        bool isValid = KeyMgr.VerifyReleaseKey(rootKey, pubKeyBytes, sigBytes);
        if (isValid)
        {
            Console.WriteLine("Signature OK");
        }
        else
        {
            Console.WriteLine("Signature verification FAILED");
        }
        return isValid;
    }

    /// <summary>
    /// Sign a release file using the private key from the release key. Expects that the public key
    /// and its signature are next to the <priv-key-file> with names '.pub' and '.sig' respectively.
    /// The root key is read from the specified file and used to verify the release public key
    /// first. The signature is written to the output file with the same name as the release
    /// file but with '.sig' appended.
    /// </summary>
    static async Task<int> SignRelease(string privKeyFile, string releaseFile)
    {
        var releaseSigFile = releaseFile + ".sig";

        var privKeyString = await File.ReadAllTextAsync(privKeyFile);
        using var releaseData = File.OpenRead(releaseFile);
        var sig = KeyMgr.SignRelease(privKeyString, releaseData);
        await File.WriteAllBytesAsync(releaseSigFile, sig);
        Console.WriteLine($"Release file signed successfully. Signature written to {releaseSigFile}");
        return 0;
    }

    /// <summary>
    /// Verify a release file against the public key. Also verifies the signature of the public key
    /// against the root key. The signature of the release file is expected to be in a file with
    /// with the same path as the release file, but with '.sig' appended. The signature of the
    /// public key is expected to be in a file with the same path, but with '.sig' appended.
    /// </summary>
    static async Task<bool> VerifyRelease(string pubKeyFile, string releaseFile)
    {
        var releaseSigFile = releaseFile + ".sig";

        using var releaseData = File.OpenRead(releaseFile);
        var sig = await File.ReadAllBytesAsync(releaseSigFile);
        Console.WriteLine("Verifying release file signature...");
        var result = KeyMgr.VerifyRelease(await File.ReadAllTextAsync(pubKeyFile), releaseData, sig);
        if (result)
        {
            Console.WriteLine("Release file OK.");
        }
        else
        {
            Console.WriteLine("Release file verification FAILED.");
        }
        return result;
    }
}