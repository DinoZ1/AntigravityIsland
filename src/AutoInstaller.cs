using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AntigravityIsland.Services
{
    public static class AutoInstaller
    {
        public static void EnsureInstalled()
        {
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string geminiConfigDir = Path.Combine(userProfile, ".gemini", "config");

                if (!Directory.Exists(geminiConfigDir))
                {
                    Directory.CreateDirectory(geminiConfigDir);
                }

                // 1. Instala o script agy-island-bridge.js na pasta .gemini/config (caminho seguro e sem acentos)
                string bridgeScriptPath = Path.Combine(geminiConfigDir, "agy-island-bridge.js");
                string bridgeCode = GetBridgeScriptContent();
                File.WriteAllText(bridgeScriptPath, bridgeCode, Encoding.UTF8);

                // Caminho formatado com barras normais para evitar problemas de escape no JSON/cmd
                string safeBridgePath = bridgeScriptPath.Replace("\\", "/");
                string nodeCmd = string.Format("node \"{0}\"", safeBridgePath);

                // 2. Atualiza ~/.gemini/config/hooks.json
                string hooksJsonPath = Path.Combine(geminiConfigDir, "hooks.json");
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> rootConfig = new Dictionary<string, object>();

                if (File.Exists(hooksJsonPath))
                {
                    try
                    {
                        string existingContent = File.ReadAllText(hooksJsonPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(existingContent))
                        {
                            rootConfig = serializer.Deserialize<Dictionary<string, object>>(existingContent) 
                                         ?? new Dictionary<string, object>();
                        }
                    }
                    catch { }
                }

                // Monta a definicao de hooks para a ilha
                Dictionary<string, object> islandHook = new Dictionary<string, object>();
                islandHook["enabled"] = true;

                // PreToolUse (intercepta chamadas de ferramentas para Allow/Deny)
                var preToolList = new List<object>();
                var preToolGroup = new Dictionary<string, object>();
                preToolGroup["matcher"] = "*";
                var preToolHooks = new List<object>();
                var preToolAction = new Dictionary<string, object>();
                preToolAction["type"] = "command";
                preToolAction["command"] = nodeCmd + " PreToolUse";
                preToolAction["timeout"] = 305;
                preToolHooks.Add(preToolAction);
                preToolGroup["hooks"] = preToolHooks;
                preToolList.Add(preToolGroup);
                islandHook["PreToolUse"] = preToolList;

                // PreInvocation (atualiza estado para Pensando)
                var preInvocList = new List<object>();
                var preInvocAction = new Dictionary<string, object>();
                preInvocAction["type"] = "command";
                preInvocAction["command"] = nodeCmd + " PreInvocation";
                preInvocAction["timeout"] = 5;
                preInvocList.Add(preInvocAction);
                islandHook["PreInvocation"] = preInvocList;

                // PostToolUse (atualiza status de conclusao de comando)
                var postToolList = new List<object>();
                var postToolGroup = new Dictionary<string, object>();
                postToolGroup["matcher"] = "*";
                var postToolHooks = new List<object>();
                var postToolAction = new Dictionary<string, object>();
                postToolAction["type"] = "command";
                postToolAction["command"] = nodeCmd + " PostToolUse";
                postToolAction["timeout"] = 5;
                postToolHooks.Add(postToolAction);
                postToolGroup["hooks"] = postToolHooks;
                postToolList.Add(postToolGroup);
                islandHook["PostToolUse"] = postToolList;

                // Stop (retorna a ilha para o estado Idle)
                var stopList = new List<object>();
                var stopAction = new Dictionary<string, object>();
                stopAction["type"] = "command";
                stopAction["command"] = nodeCmd + " Stop";
                stopAction["timeout"] = 10;
                stopList.Add(stopAction);
                islandHook["Stop"] = stopList;

                rootConfig["antigravity-dynamic-island"] = islandHook;

                string updatedJson = serializer.Serialize(rootConfig);
                File.WriteAllText(hooksJsonPath, updatedJson, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Falha silenciosa para nao interromper o arranque
                System.Diagnostics.Debug.WriteLine("Erro na autoinstalacao: " + ex.Message);
            }
        }

        private static string GetBridgeScriptContent()
        {
            return @"'use strict';
/**
 * Antigravity Dynamic Island - Hook Bridge Autogerado
 */
const http = require('node:http');

const eventType = process.argv[2] || 'PreToolUse';
const PORT = 47823;

let inputData = '';
process.stdin.setEncoding('utf8');

process.stdin.on('data', chunk => {
  inputData += chunk;
});

process.stdin.on('end', () => {
  let payload = {};
  try {
    payload = JSON.parse(inputData || '{}');
  } catch (err) {
    payload = {};
  }

  const postBody = JSON.stringify({
    agent: 'antigravity',
    eventType: eventType,
    conversationId: payload.conversationId || '',
    toolCall: payload.toolCall || null,
    stepIdx: payload.stepIdx || 0,
    modelName: payload.modelName || '',
    pid: process.ppid
  });

  const req = http.request({
    hostname: '127.0.0.1',
    port: PORT,
    path: '/api/hook',
    method: 'POST',
    timeout: eventType === 'PreToolUse' ? 300000 : 4000,
    headers: {
      'Content-Type': 'application/json',
      'Content-Length': Buffer.byteLength(postBody)
    }
  }, res => {
    let responseBody = '';
    res.setEncoding('utf8');

    res.on('data', chunk => {
      responseBody += chunk;
    });

    res.on('end', () => {
      let result = { decision: 'allow' };
      try {
        const parsed = JSON.parse(responseBody || '{}');
        if (parsed.decision) {
          result = parsed;
        } else if (parsed.hookSpecificOutput && parsed.hookSpecificOutput.decision) {
          result = {
            decision: parsed.hookSpecificOutput.decision.behavior || 'allow',
            reason: parsed.hookSpecificOutput.decision.message || ''
          };
        }
      } catch (e) {
        result = { decision: 'allow' };
      }

      process.stdout.write(JSON.stringify(result));
      process.exit(0);
    });
  });

  req.on('error', () => {
    if (eventType === 'PreToolUse') {
      process.stdout.write(JSON.stringify({ decision: 'allow' }));
    } else if (eventType === 'PreInvocation') {
      process.stdout.write(JSON.stringify({ injectSteps: [] }));
    } else {
      process.stdout.write(JSON.stringify({}));
    }
    process.exit(0);
  });

  req.on('timeout', () => {
    req.destroy();
    process.stdout.write(JSON.stringify({ decision: 'allow' }));
    process.exit(0);
  });

  req.write(postBody);
  req.end();
});
";
        }
    }
}
