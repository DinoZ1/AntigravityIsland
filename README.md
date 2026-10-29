# 🏝️ Antigravity Dynamic Island (Clean-Room Native)

> **Interface Flutuante Inteligente, Mascote Robô e Controlador de Permissões em Tempo Real para o Antigravity CLI (`agy`)**

Construção nativa e independente (Clean-Room) para Windows 10 e 11. Substitui integralmente o runtime pesado do Electron por uma aplicação Win32/WPF ultraleve acelerada por GPU, consumindo quase zero de memória e CPU.

---

## ⚡ Como Usar em Outro Computador (Passo a Passo)

Esta pasta é **100% autossuficiente e portátil**. Você pode copiá-la para um pendrive, Google Drive ou outro computador e rodar direto!

### 1. Iniciar a Ilha
Dê dois cliques em:
* **`Iniciar.bat`** (ou diretamente no executável **`AntigravityIsland.exe`**).

> **Nota de Auto-Instalação:**  
> Na primeira vez em que é aberto em qualquer computador, o aplicativo detecta o ambiente e instala automaticamente a ponte de hooks em `%USERPROFILE%\.gemini\config\hooks.json`. Você não precisa configurar nada manualmente!

### 2. Encerrar a Ilha
* Clique no botão **`✕`** na pílula, ou
* Dê dois cliques em **`Fechar.bat`**.

### 3. Recompilar o Código (Opcional)
Se fizer qualquer modificação nos arquivos `.cs` da pasta `src/`:
* Dê dois cliques em **`build.bat`**.
* O executável será recompilado em menos de 2 segundos usando o `csc.exe` nativo do próprio Windows (sem precisar instalar Visual Studio nem SDKs extras).

---

## 📊 Comparativo: TwoCode original vs. Antigravity Dynamic Island

| Métrica | TwoCode Original | Antigravity Dynamic Island | Vantagem |
| :--- | :--- | :--- | :--- |
| **Arquitetura** | Electron (Chromium + Node.js) | C# .NET / Win32 WPF Nativo | Sem navegador embutido |
| **Tamanho do Arquivo** | ~245 MB | **~57 KB** | **99,97% menor** |
| **Uso de Memória RAM** | ~220 MB - 300 MB | **~20 MB - 25 MB** | **90% menos RAM** |
| **Uso de CPU Ociosa** | 0,5% a 2% | **0,0%** (Event-driven) | Zero aquecimento |
| **Tempo de Abertura** | ~2,5 segundos | **< 100 milissegundos** | Instantâneo |
| **Instaladores Necessários** | Sim | **Nenhum** (Nativo do Windows) | Portátil |

---

## 🎯 Principais Recursos

### 1. Pílula Inteligente & Retração Automática
* **Em repouso**: Fica compacta no topo da tela com iluminação de status.
* **Ao passar o mouse (Hover)**: O dashboard completo se expande suavemente com animação fluida (Cubic Ease).
* **Ao tirar o mouse**: Retrai imediatamente (120ms) de volta para a pílula compacta.
* **Transparência Total (Pass-Through)**: O fundo ao redor da pílula não bloqueia o cursor. Você clica normalmente em abas do Chrome ou janelas abaixo.

### 2. Agente de Voz & Ditado
* Clique no **Mascote Robô** ou no botão **`🎙️ Falar`** para ditar comandos por voz usando o atalho nativo do Windows (Win + H).
* O comando é enviado para o Antigravity CLI (`agy -p "seu comando"`).
* O Robô responde sintetizando a resposta em áudio em português natural (`Microsoft Maria`).
* Botão **`■ Parar`** e barra equalizadora de voz animada.

### 3. Interceptação e Controle de Permissões (Allow / Deny)
* Quando o Antigravity CLI precisa executar um comando de terminal ou alterar arquivos:
  * A ilha exibe os botões rápidos **Allow [Enter]** e **Deny [Esc]**.
  * A resposta é injetada instantaneamente de volta no processo CLI sem que você precise alternar janelas.

### 4. Captura de Tela Rápida (Contexto Visual)
* Clique no botão **`📷 Print`** ou no card de **Settings**:
  * Tira uma captura em alta resolução com compensação DPI (Per-Monitor v2).
  * O caminho do arquivo PNG gerado é automaticamente copiado para o seu **Clipboard** para colar diretamente no CLI ou chat.

### 5. Widgets Embutidos
* **Relógio Digital**: Horário e data do sistema em tempo real.
* **Pomodoro / Focus Timer**: Contador regressivo de 25 minutos para foco.
* **Indicador Spotify**: Card estilizado de mídia.

---

## 📁 Estrutura desta Pasta

```text
AntigravityIsland/
├── AntigravityIsland.exe    # Executável nativo compilado (pronto para rodar)
├── Iniciar.bat             # Atalho de 1 clique para iniciar a ilha
├── Fechar.bat              # Atalho de 1 clique para fechar o processo
├── build.bat               # Compilador nativo Windows (gera AntigravityIsland.exe)
├── build.ps1               # Versão PowerShell do compilador
├── install-hooks.ps1       # Script opcional para gerenciar hooks manualmente
├── README.md               # Esta documentação
│
├── src/                    # Código-fonte limpo em C# (Clean-Room)
│   ├── Program.cs          # Ponto de entrada e gerenciamento de instância única
│   ├── IslandWindow.cs     # Interface WPF, robô vetorial, animações e widgets
│   ├── IpcServer.cs        # Servidor HTTP local (porta 47823) para escutar o CLI
│   ├── AutoInstaller.cs    # Auto-configurador de hooks e bridge em ~/.gemini/config
│   ├── AntigravityCli.cs   # Módulo de invocação do CLI (agy -p)
│   ├── VoiceAgent.cs       # Síntese e reconhecimento de fala nativo do Windows
│   ├── ScreenCapture.cs    # Captura de tela com DWM e High-DPI GDI
│   ├── AppState.cs         # Modelos de estado (Idle, Thinking, WaitingPermission)
│   └── NativeWin32.cs      # P/Invoke Win32 (WS_EX_NOACTIVATE, SWP_TOPMOST, etc.)
│
└── hooks/                  # Scripts de ponte utilizados pelo Antigravity CLI
    ├── agy-island-bridge.js
    ├── agy-island-bridge.ps1
    └── hooks.json
```

---

## 💡 Dica de Inicialização com o Windows
Se quiser que a ilha ligue automaticamente sempre que você ligar o computador:
1. Pressione `Win + R`, digite `shell:startup` e dê Enter.
2. Crie um atalho do arquivo `Iniciar.bat` (ou `AntigravityIsland.exe`) dentro da pasta que se abriu.
Pronto! Toda vez que o Windows iniciar, sua Dynamic Island estará lá no topo pronta para te auxiliar.
