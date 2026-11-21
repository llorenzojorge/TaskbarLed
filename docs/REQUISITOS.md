# TaskbarLed — Requisitos

Painel estilo terminal (originalmente um LED estilo Matrix) na área livre da barra de tarefas do Windows 11 que mostra, em tempo real, o andamento das sessões do Claude Code (app desktop, CLI e extensão do VS Code) enquanto a janela do Claude não está visível.

## 1. Requisitos funcionais

### Visibilidade
- **RF01** — O painel aparece quando a janela do Claude **não está visível**: minimizada **ou** coberta por outras janelas.
- **RF02** — O painel se oculta quando o Claude está visível, inclusive em **tela dividida** (snap) ao lado de outro app. Critério: porcentagem da área da janela do Claude realmente visível ≥ limiar configurável.
- **RF03** — O painel não aparece sobre apps em tela cheia (vídeo, jogos, apresentações).

### Conteúdo
- **RF04** — Estado por sessão: ociosa, rodando, concluída, aguardando aprovação/pergunta.
- **RF05** — Cronômetro do prompt atual (tempo desde o envio).
- **RF06** — Letreiro com a atividade atual: ferramenta em uso (ex.: "Editando scraper.py", "Rodando npm test") e trecho da última mensagem escrita pelo Claude (atualizado por mensagem, não por token).
- **RF07** — Uso do contexto da conversa (v1). Limite do plano (5h/semanal) fica para versão futura — ver seção 5.

### Múltiplas sessões
- **RF08** — "Canal" de barrinhas: uma barrinha por sessão, com o estado codificado visualmente (acesa = rodando, piscando verde = concluída, piscando âmbar = aguardando você, apagada = ociosa).
- **RF09** — O letreiro alterna entre as sessões ativas; sessões que aguardam o usuário têm prioridade.
- **RF10** — Visual limpo, sem poluição: no máximo uma linha de texto e indicadores compactos.

### Interação
- **RF11** — Sessão concluída: o painel pisca verde **até o usuário abrir o Claude**.
- **RF12** — Clique no painel abre/traz o Claude para frente (na sessão em destaque, se possível).
- **RF13** — Modo edição (pelo menu da bandeja): arrastar e redimensionar o painel dentro da barra de tarefas; posição e tamanho persistidos.
- **RF14** — Seleção do monitor: principal, um específico ou todos.

### Sistema
- **RF15** — Inicia automaticamente com o Windows e abre sozinho.
- **RF16** — Ícone na bandeja com menu: editar posição, monitor, tema, pausar, sair.
- **RF17** — Instalação/remoção dos hooks do Claude Code por comando, **sem sobrescrever** hooks existentes do usuário (idempotente e reversível).

## 2. Requisitos não funcionais
- **RNF01** — Leve: meta de ≤ 50 MB de RAM e ~0% de CPU em repouso.
- **RNF02** — Hooks rápidos: o executável de hook não pode atrasar o Claude (meta < 50 ms, falha silenciosa se o painel estiver fechado).
- **RNF03** — Código aberto no GitHub: arquitetura em camadas, extensível, testada, com CI e documentação de contribuição.
- **RNF04** — Privacidade: nenhum dado sai da máquina; tudo é local.

## 3. Decisões técnicas
| Decisão | Escolha | Motivo |
|---|---|---|
| Plataforma | .NET 10 (LTS) + WPF | Moderno, padrão de mercado, bons efeitos visuais |
| Comunicação hook → painel | Named pipe + arquivo de estado por sessão | Tempo real, com backup se o painel estiver fechado |
| Nome | TaskbarLed | Neutro; permite suportar outras ferramentas de IA |
| Tema inicial | Claude Code (terminal quente, ✻ terracota). Até a v1.1.0 era o verde terminal (Matrix), que continua disponível | Escolha do usuário; trocado em 2026-11-15 para ter a identidade do Claude Code |

## 4. Arquitetura (proposta)
```
src/
  TaskbarLed.Core/                 domínio puro: Session, SessionState, prioridade, agregação
  TaskbarLed.Sources.ClaudeCode/   hooks + transcript + status line → eventos de domínio (ISessionSource)
  TaskbarLed.Platform.Windows/     Win32: barra de tarefas, oclusão/visibilidade, monitores, autostart, tela cheia
  TaskbarLed.App/                  WPF: painel, temas (ITheme), modo edição, bandeja
  TaskbarLed.Hook/                 executável mínimo chamado pelos hooks do Claude Code
tests/
  TaskbarLed.Core.Tests/ ...       xUnit
```
Pontos de extensão para contribuidores: `ISessionSource` (outras IAs), temas como `ResourceDictionary` com as chaves `Led.*` (novos visuais), `IClaudeVisibilityProbe` (regras de exibição) e `IUsageLimitProvider` (limite do plano, futuro). Detalhes em [ARCHITECTURE.md](ARCHITECTURE.md).

## 5. Verificações de viabilidade
| Item | Status |
|---|---|
| Claude Code expõe limite 5h / semanal / contexto | ✅ Confirmado (status line JSON: `rate_limits.five_hour`, `rate_limits.seven_day`, `context_window.used_percentage`, Claude Code 2.1.325) |
| App desktop dispara hooks do `~/.claude/settings.json` | ✅ Confirmado: `PreToolUse` dispara em tempo real, sem reiniciar; payload traz `session_id`, `transcript_path`, `cwd`, `prompt_id`, `tool_name` |
| `UserPromptSubmit`, `Stop`, `SessionStart` no app desktop | ✅ Confirmado. `Stop` já traz `last_assistant_message`; `UserPromptSubmit` traz `session_title` |
| Hooks na extensão do VS Code | ✅ Confirmado (mesmos eventos, mesmo formato) |
| App desktop / VS Code executam a status line | ❌ Não executam. O limite de 5h/semanal **não** chega por essa via; precisa de outra fonte (ver decisão pendente abaixo) |
| Uso do contexto da conversa | ✅ Calculável pelos campos `usage` do transcript (`transcript_path`) |

### Observações para a implementação
- O hook deve ler o stdin como **UTF-8** (no teste, o PowerShell leu na codepage do console e corrompeu acentos).
- `Notification` (aprovação/pergunta) não apareceu no teste porque nenhuma aprovação foi pedida; validar no desenvolvimento.

### Decisão: fonte do limite do plano (5h/semanal)
**Decidido (2026-11-14): opção 1.** A v1 mostra apenas o uso do contexto da conversa. O limite do plano fica como recurso futuro, atrás de uma interface própria (`IUsageLimitProvider`) para que um provedor opt-in possa ser adicionado depois sem mudar o núcleo.

Opções avaliadas:
1. Exibir apenas o uso do contexto (100% local, sem riscos) e deixar o limite do plano como recurso futuro.
2. Consultar o endpoint de uso da conta usando o token OAuth local do Claude Code: dado real, mas implica ler credenciais do usuário — exige opt-in explícito e cuidado num projeto open source.
3. Para usuários da CLI no terminal, usar a status line (onde ela é executada).

**Revisto (2026-11-21): o uso do contexto saiu do painel.** O número divergia do que o app do Claude mostra (85% no painel, 29% no app para o mesmo chat): o cálculo supunha a janela de contexto a partir do nome do modelo, e isso não acompanha os modelos atuais. Um número errado confunde mais do que ajuda. O limite do plano continua fora (opção 2 rejeitada de novo: exigiria ler o token da conta e acessar a rede). No lugar, o cronômetro do prompt desceu para a linha do feed e as abas ganharam a linha de cima inteira para os títulos dos chats.
| .NET 10 SDK | ⏳ Instalado apenas o .NET 8; .NET 10 precisa ser instalado |
| Janela do Claude identificável | ✅ Processo `claude.exe` (pacote MSIX), janela com título "Claude" |
