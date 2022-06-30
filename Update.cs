
using System.Diagnostics;
using System.Threading.Tasks;
using Serde;
using Serde.Json;

namespace Dnvm;

sealed partial class Update
{
    private readonly Logger _logger;
    private readonly Command.UpdateOptions _options;

    public Update(Logger logger, Command.UpdateOptions options)
    {
        _logger = logger;
        _options = options;
    }

    [GenerateDeserialize]
    partial struct LatestReleaseResponse
    {
        public string assets_url { get; init; }
    }

    public Task<int> Handle()
    {
        if (!_options.Self)
        {
            _logger.Error("update is currently only supported with --self");
            return Task.FromResult(1);
        }

        _logger.Error("Not currently supported");
        return Task.FromResult(1);
    }
}