namespace Dnvm;

enum LogLevel
{
	Normal = 1,
	Info = 2
}

internal interface ILogger
{
	LogLevel LogLevel { set; }
	void Info(string message);
	void Log(string message);
	void Error(string message);
}