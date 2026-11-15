if(File.Exists(Path.Combine(AppContext.BaseDirectory,"FAIL_START")))return 3;
var home=Environment.GetEnvironmentVariable("DESKSYNC_UPDATER_TEST_HOME")??throw new Exception("This fixture must run inside the isolated update test.");
var nonce=Environment.GetEnvironmentVariable("DESKSYNC_UPDATE_NONCE");if(nonce!=null){Directory.CreateDirectory(Path.Combine(home,"updates"));File.WriteAllText(Path.Combine(home,"updates","health-"+nonce+".txt"),"fixture ready");}
File.WriteAllText(Path.Combine(home,"fixture-started.txt"),"started");Thread.Sleep(1500);return 0;
