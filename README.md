# TimeTracker

A minimal Windows desktop time tracker built with C#, .NET 10, WPF, and SQLite.

## Build and run

From Command Prompt at the repository root, build with:

	dotnet build TimeTracker\TimeTracker.csproj

Run the project from Visual Studio or VS Code, or launch the generated executable under `TimeTracker\bin\Debug\net10.0-windows\TimeTracker.exe`.

## Use

1. On first launch, choose **Create database…** or **Select database…**. The selected database is remembered and reopened on the next launch (if it still exists).
2. Add a work item, then select it from the list. Use the search box to filter items by name.
3. Choose **Start working** and then **Stop session** when finished. The current-session counter displays hours, minutes, and seconds.
4. The list shows each item's accumulated time in minutes. To remove an item and its saved sessions, choose **Delete** and confirm.

Stopped sessions are rounded up to a whole minute (a session shorter than one minute records as one minute). Only one session can run at a time. An active session remains active if the app closes, and its counter resumes when the database is reopened.

## Database

The database contains work items and their work sessions. See [DatabaseModel.md](DatabaseModel.md) for the PlantUML schema overview.
