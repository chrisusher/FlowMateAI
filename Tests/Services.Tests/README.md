# Services.Tests

The repository tests use the Linux Azure Cosmos DB emulator through Testcontainers. Docker Desktop or another Docker-compatible runtime must be installed and running before executing the test project.

The test setup starts the emulator once for the run, creates a unique Cosmos database for each persistence test, and deletes each database during teardown. The emulator container is removed when the test run finishes. If Docker is unavailable, the test fixture reports the required setup instead of silently falling back to an in-memory provider.

Run the tests with:

```powershell
dotnet test Tests/Services.Tests/Services.Tests.csproj --configuration Release
```
