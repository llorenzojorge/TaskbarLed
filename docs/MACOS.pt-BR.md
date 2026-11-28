# TaskbarLed no macOS

[English](MACOS.md) · [Galeria de temas](MACOS-THEMES.pt-BR.md)

## Requisitos

A prévia para Mac exige macOS 14 ou posterior. O pacote Apple Silicon (arm64, M1 em diante) foi testado nativamente num M2 com macOS 26.5.2. O pacote Intel (x64) foi compilado para Intel e testado via Rosetta nesse M2. Macs Intel físicos e outras versões do macOS ainda precisam de teste direto.

## Instalar

Quando os pacotes macOS forem publicados, baixe o `.pkg` adequado nas [releases do projeto original](https://github.com/llorenzojorge/TaskbarLed/releases). Escolha arm64 para chip Apple ou x64 para Intel. Abra o pacote, siga o assistente e abra **TaskbarLed.app** em Aplicativos. Se usar o ZIP, extraia e mova o app para Aplicativos antes de abrir. Cada pacote inclui o runtime .NET.

A prévia está sem assinatura e sem notarização. Se o macOS disser que a Apple não pôde verificar o instalador, compare o SHA-256 dele com o da release. Depois de tentar abri-lo, use **Ajustes do Sistema → Privacidade e Segurança → Abrir Mesmo Assim**. Também é possível revisar o código na tag da versão e compilar localmente. [Instruções da Apple](https://support.apple.com/pt-br/102445)

## Compilar a partir do código

Instale o SDK .NET indicado em `global.json` e as ferramentas de linha de comando da Apple. Na raiz do repositório, execute o comando para o seu Mac:

```sh
src/TaskbarLed.App.Avalonia/build-macos.sh osx-arm64
src/TaskbarLed.App.Avalonia/build-macos.sh osx-x64
```

O script cria um `.pkg` e um `.zip` em `artifacts/TaskbarLed-<rid>/`. Encerre a cópia anterior antes de instalar uma atualização.

## Usar o painel

O TaskbarLed flutua acima do Dock. O painel aparece quando há atividade. O ícone da barra de menus permite mantê-lo visível mesmo ocioso. Clique com o botão direito para abrir **Temas**, **Editar posição e tamanho** ou **Encerrar**. Escolha o tamanho automático ou arraste a borda direita, inferior ou o canto no modo manual. A opacidade ajusta o fundo do tema e mantém o texto legível. Sessões concluídas aparecem por dez segundos. No Mapa de Linhas, a letra da rota permanece enquanto a sessão estiver visível. Liquid Glass usa material nativo fosco no macOS 26. Escolha **Crystal Liquid** para uma superfície mais transparente, com menos tonalização. Versões anteriores usam um fundo translúcido.

Cada sessão e subagente mostra o tempo decorrido quando há uma hora de início conhecida. O til indica o tempo desde que o painel viu o agente do Antigravity pela primeira vez.

Ative **Modo Dynamic Island** pelo menu do painel ou da barra de menus para encaixar o painel no topo do monitor principal, sobre o recorte da câmera quando houver. O modo abre como uma barra compacta com a marca e o número de atividades. Clique na barra para expandir ou recolher as sessões. O tema selecionado continua aplicado. Desative-o para voltar à posição salva.

## Conectar agentes

O painel lê a atividade local de Octo, Codex e Antigravity. Os subagentes aparecem sob a sessão principal mesmo quando ela está ociosa. A detecção de executáveis pode identificar uma ferramenta aberta sem informar sessões individuais. Para incluir um processo personalizado, adicione o nome do executável e um rótulo em `~/Library/Application Support/TaskbarLed/agents.json`.

Para o Claude Code, selecione **Ativar integração com Claude Code…** no menu e reinicie as sessões. O app preserva as configurações e os hooks existentes.

Outros orquestradores podem encaminhar eventos pelo hook incluído. Execute a ponte no Mac, inclusive para receber eventos de um agente remoto. Cada chamada lê um objeto JSON da entrada padrão:

```sh
hook="/Applications/TaskbarLed.app/Contents/MacOS/TaskbarLed.Hook"
printf '%s\n' '{"agent":"pipeline","session_id":"job-42","status":"started","title":"Build"}' | "$hook" agent
```

Use um `agent` estável e um `session_id` único por trabalho simultâneo. Os estados aceitos são `started`, `running`, `waiting`, `done` e `ended`. Os campos opcionais `title` e `message` definem o texto exibido. Use `parent_session_id` para agrupar um subagente sob uma sessão com o mesmo `agent`. Entradas acima de 2 MiB são descartadas.

## Desinstalar

Desative a integração com Claude Code no menu, caso esteja ativa, e selecione **Encerrar**. Mova o aplicativo de Aplicativos para o Lixo pelo Finder. O macOS pode pedir autorização de administrador para removê-lo. As preferências permanecem em `~/Library/Application Support/TaskbarLed`.

## Publicar

O job do macOS gera um `.pkg` e um `.zip` por arquitetura e anexa os pacotes e seus SHA-256 à mesma release `vX.Y.Z` do Windows.
