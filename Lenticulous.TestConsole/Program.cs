// Lenticulous desktop test runner.
// Uses SpawnDev.UnitTesting's ConsoleRunner to enumerate and run the [TestMethod]s of Lenticulous.Tests.
// Compatible with PlaywrightMultiTest's console contract: no arguments lists the tests, one argument runs that test.
using Lenticulous.Tests;
using Microsoft.Extensions.DependencyInjection;
using SpawnDev.UnitTesting;

var services = new ServiceCollection();
services.AddSingleton<ScanLogicTests>();
var sp = services.BuildServiceProvider();
var runner = new UnitTestRunner(sp, true);
await ConsoleRunner.Run(args, runner);
