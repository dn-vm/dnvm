using StaticCs;
using System;
using System.CommandLine;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Dnvm
{
	internal class Init
	{
		public record struct Options(Shell Shell, bool Copyable, bool RegisterCompletions);

		public static Command Command
		{
			get
			{
				Command init = new("init", "Prints the shell commands to add dnvm and the active sdk to the PATH.");

				Argument<Shell> shell = new Argument<Shell>("shell");
				init.Add(shell);

				Option<bool> copyable = new(new[] { "--copyable", "-c" }, "Print the line to be copied and pasted into your profile to add the active sdk to the PATH.");
				init.Add(copyable);

				init.SetHandler(Handle, shell, copyable);
				return init;
			}
		}

		ILogger _logger;
		Options _options;
		Manifest _manifest;

		internal static Task<int> Handle(Shell shell, bool copyable)
		{
			var init = new Init(Logger.Default, ManifestHelpers.Instance, new Options(shell, copyable, true));
			return init.Handle();
		}

		public Task<int> Handle()
		{
			if (_options.Copyable)
			{
				_logger.Log(ProfileText(_options.Shell));
				return Task.FromResult(0);
			}
			string output = "";
			if (_options.RegisterCompletions)
			{
				output += "dotnet tool install --global dotnet-suggest";
				output += RegisterCompletionsText(_options.Shell);
			}
			output = AddToPathText(_options.Shell, Path.GetDirectoryName(Utilities.ProcessPath)!);
			output += ActivateAliasText(_options.Shell);

			string? activePath = _manifest.Active?.Path;
			if (!string.IsNullOrEmpty(activePath))
				output += AddToPathText(_options.Shell, activePath);

			_logger.Log(output);
			return Task.FromResult(0);
		}

		private string RegisterCompletionsText(Shell shell)
		{
			return shell switch
			{
				Shell.Powershell => PowershellSuggestShim,
				Shell.Bash => BashDotnetSuggest,
				Shell.Zsh => ZshDotnetSuggestShim
			};
		}

		private string PowershellSuggestShim => $$$"""
			# dotnet suggest shell start
			if (Get-Command "dotnet-suggest" -errorAction SilentlyContinue)
			{
				$availableToComplete = (dotnet-suggest list) | Out-String
				$availableToCompleteArray = $availableToComplete.Split([Environment]::NewLine, [System.StringSplitOptions]::RemoveEmptyEntries)

				Register-ArgumentCompleter -Native -CommandName $availableToCompleteArray -ScriptBlock {
					param($wordToComplete, $commandAst, $cursorPosition)
					$fullpath = (Get-Command $commandAst.CommandElements[0]).Source

					$arguments = $commandAst.Extent.ToString().Replace('"', '\"')
					dotnet-suggest get -e $fullpath --position $cursorPosition -- "$arguments" | ForEach-Object {
						[System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
					}
				}    
			}
			else
			{
				"Unable to provide System.CommandLine tab completion support unless the [dotnet-suggest] tool is first installed."
				"See the following for tool installation: https://www.nuget.org/packages/dotnet-suggest"
			}

			$env:DOTNET_SUGGEST_SCRIPT_VERSION = "1.0.2"
			# dotnet suggest script end
		""";
		private string ZshDotnetSuggestShim => $$$"""
			# dotnet suggest shell complete script start
			_dotnet_zsh_complete()
			{
				# debug lines, uncomment to get state variables passed to this function
				# echo "\n\n\nstate:\t'$state'"
				# echo "line:\t'$line'"
				# echo "words:\t$words"

				# Get full path to script because dotnet-suggest needs it
				# NOTE: this requires a command registered with dotnet-suggest be
				# on the PATH
				full_path=`which ${words[1]}` # zsh arrays are 1-indexed
				# Get the full line
				# $words array when quoted like this gets expanded out into the full line
				full_line="$words"

				# Get the completion results, will be newline-delimited
				completions=$(dotnet suggest get --executable "$full_path" -- "$full_line")
				# explode the completions by linefeed instead of by spaces into the descriptions for the
				# _values helper function.
				
				exploded=(${(f)completions})
				# for later - once we have descriptions from dotnet suggest, we can stitch them
				# together like so:
				# described=()
				# for i in {1..$#exploded}; do
				#     argument="${exploded[$i]}"
				#     description="hello description $i"
				#     entry=($argument"["$description"]")
				#     described+=("$entry")
				# done
				_values 'suggestions' $exploded
			}

			# apply this function to each command the dotnet-suggest knows about
			compdef _dotnet_zsh_complete $(dotnet-suggest list)

			export DOTNET_SUGGEST_SCRIPT_VERSION="1.0.0"
			# dotnet suggest shell complete script end
			""";

		private string BashDotnetSuggest => $$$$"""
			# dotnet suggest shell complete script start
			_dotnet_bash_complete()
			{
				local fullpath=`type -p ${COMP_WORDS[0]}`
				local escaped_comp_line=$(echo "$COMP_LINE" | sed s/\"/'\\\"'/g)
				local completions=`dotnet-suggest get --executable "${fullpath}" --position ${COMP_POINT} -- "${escaped_comp_line}"`

				if [ "${#COMP_WORDS[@]}" != "2" ]; then
					return
				fi

				local IFS=$'\n'
				local suggestions=($(compgen -W "$completions"))

				if [ "${#suggestions[@]}" == "1" ]; then
					local number="${suggestions[0]/%\ */}"
					COMPREPLY=("$number")
				else
					for i in "${!suggestions[@]}"; do
						suggestions[$i]="$(printf '%*s' "-$COLUMNS"  "${suggestions[$i]}")"
					done

					COMPREPLY=("${suggestions[@]}")
				fi
			}

			_dotnet_bash_register_complete()
			{
				local IFS=$'\n'
				complete -F _dotnet_bash_complete `dotnet-suggest list`
			}
			_dotnet_bash_register_complete
			export DOTNET_SUGGEST_SCRIPT_VERSION="1.0.1"
			# dotnet suggest shell complete script end
			""";

		public static string ProfileText(Shell shell, string? dnvmPath = null)
			=> shell switch
			{
				Shell.Powershell => $"Invoke-Expression -Command $({dnvmPath ?? Utilities.ProcessPath} init powershell | out-string)",
				Shell.Bash => $"eval \"$({dnvmPath ?? Utilities.ProcessPath} init bash)\"",
				Shell.Zsh => $"eval \"$({dnvmPath ?? Utilities.ProcessPath} init zsh)\""
			};

		public Init(ILogger logger, Manifest manifest, Options options)
		{
			_logger = logger;
			_manifest = manifest;
			_options = options;
		}

		// TODO: add an alias to evaluate the init script after calling dnvm activate
		static string ActivateAliasText(Shell shell)
		{
			return "";
			//throw new NotImplementedException();
		}

		static string AddToPathText(Shell shell, string path)
			=> shell switch
			{
				Shell.Powershell => PowershellAddToPathText(path),
				Shell.Bash => BashAddToPathText(path),
				Shell.Zsh => ZshAddToPathText(path)
			} + Environment.NewLine;

		static string PowershellAddToPathText(string pathToAdd)
			=> $"$env:PATH=\"{pathToAdd};$env:PATH\"";

		static string BashAddToPathText(string pathToAdd)
			=> Utilities.CurrentOS == OSPlatform.Windows ?
				$"export PATH=\"{ConvertWindowPathToGitBash(pathToAdd)}:$PATH\""
				: $"export PATH=\"{pathToAdd}:$PATH\"";

		static string ConvertWindowPathToGitBash(string path)
			=> $"/{path[0].ToString().ToLower()}{path[2..].Replace('\\', '/')}";

		static string ZshAddToPathText(string pathToAdd)
			=> $"export PATH=\"{ConvertWindowPathToGitBash(pathToAdd)}:$PATH\"";

		public void Activate(Workload workload)
		{
			var envPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)!;
			envPath = ReplaceOrAddActiveWorkloadInPath(envPath, workload.Path);
			Environment.SetEnvironmentVariable("PATH", envPath, EnvironmentVariableTarget.User);
		}

		public static string ReplaceOrAddActiveWorkloadInPath(string path, string newActivePath)
		{
			string flag = "#dnvm-active;";
			int firstFlagStart = path.IndexOf(flag);
			// If flag not there, add to front
			if (firstFlagStart == -1)
				return flag + newActivePath + ";" + flag + path;
			int startReplace = firstFlagStart + flag.Length;
			int endReplace = path.LastIndexOf(flag);
			path = path.Remove(startReplace, endReplace - startReplace);
			path = path.Insert(startReplace, newActivePath + ';');
			return path;
		}

		[Closed]
		internal enum Shell
		{
			Powershell,
			Bash,
			Zsh
		}
	}
}
