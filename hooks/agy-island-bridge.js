'use strict';
/**
 * Antigravity Dynamic Island - Hook Bridge
 * Intercepta eventos de ciclo de vida do Antigravity CLI e conecta via HTTP local
 * com a interface Dynamic Island (127.0.0.1:47823).
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

  // Prepara o objeto unificado para a ilha
  const postBody = JSON.stringify({
    agent: 'antigravity',
    eventType: eventType,
    conversationId: payload.conversationId || '',
    toolCall: payload.toolCall || null,
    stepIdx: payload.stepIdx || 0,
    modelName: payload.modelName || '',
    pid: process.ppid // PID do processo terminal pai
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

      // Devolve para o Antigravity CLI via stdout
      process.stdout.write(JSON.stringify(result));
      process.exit(0);
    });
  });

  req.on('error', () => {
    // Se a Dynamic Island nao estiver aberta, responde 'allow' para nao travar o Antigravity CLI
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
