using System;
using Server.MirEnvir;
class P { static void Main() {
  System.IO.Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
  var e = new Envir(); e.LoadDB();
  Console.WriteLine("QuestInfoList.Count=" + Envir.Main.QuestInfoList.Count);
  int i=0;
  foreach (var q in Envir.Main.QuestInfoList) { if (i++ >= 5) break; Console.WriteLine($"  Idx={q.Index} Name={q.Name} NPC={q.NpcIndex} Type={q.Type}"); }
}}
