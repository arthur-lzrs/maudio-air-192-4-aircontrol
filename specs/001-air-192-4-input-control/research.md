# Research: Input Monitoring & Control Panel for AIR 192|4

## 1. Plataforma e linguagem

**Decision**: C# / .NET 8 com WPF para a UI desktop no Windows.

**Rationale**:
- WPF tem o melhor suporte nativo a acessibilidade no Windows (UI Automation), atendendo diretamente ao FR-020 (navegação por teclado, leitor de tela, contraste) sem bibliotecas extras.
- .NET 8 é a versão LTS atual, com suporte de longo prazo e boa integração com WASAPI via bibliotecas maduras.
- C#/.NET é a escolha padrão para apps Windows nativos que precisam de baixa latência de áudio e boa integração com o sistema (bandeja, single-instance, persistência local).

**Alternatives considered**:
- **Electron/JS**: acessibilidade e áudio de baixa latência são mais difíceis de garantir; overhead de runtime maior; rejeitado por não atender bem SC-002 (resposta de meter em 100ms) nem FR-020.
- **C++ nativo (Win32/JUCE)**: melhor controle de latência, mas desenvolvimento e manutenção mais lentos; acessibilidade exige mais trabalho manual. Guardado como opção futura se o driver-level control (fora de escopo) exigir isso.
- **WinUI 3**: mais moderno, mas ecossistema de acessibilidade e maturidade de bindings de áudio ainda menos consolidado que WPF em 2026.

## 2. Acesso ao áudio (captura e monitoramento)

**Decision**: NAudio (WASAPI, modo compartilhado/exclusivo conforme necessário) para captura dos dois inputs do AIR 192|4 e para reprodução (playthrough) no dispositivo de saída selecionado.

**Rationale**:
- NAudio é a biblioteca .NET mais madura para WASAPI, com suporte a captura por dispositivo, enumeração de dispositivos de entrada/saída, e baixa latência suficiente para a meta de 100ms (SC-002).
- Permite processar os dois canais de entrada do AIR 192|4 como um único stream estéreo (interface é 2 in / 2 out), o que mapeia diretamente para "Input 1" e "Input 2" do spec.
- Suporta cálculo de picos e RMS por buffer de áudio, necessário para os meters (FR-005a).

**Alternatives considered**:
- **CSCore**: alternativa viável, mas comunidade e manutenção mais fracas que NAudio.
- **ASIO direto**: menor latência, mas exige driver ASIO específico e adiciona complexidade de instalação; WASAPI em modo exclusivo já atende a meta de 100ms sem essa dependência extra. Pode ser revisitado se testes de latência mostrarem necessidade.

## 3. Detecção de conexão/desconexão do dispositivo

**Decision**: `MMDeviceEnumerator` do NAudio/Core Audio APIs com um `IMMNotificationClient` para eventos de adição/remoção de dispositivo, filtrando pelo nome/ID do AIR 192|4.

**Rationale**: Atende FR-001, FR-015, FR-016 e SC-005 (detecção em <3s) através de eventos nativos do Windows Core Audio, evitando polling ineficiente.

**Alternatives considered**: Polling periódico da lista de dispositivos — mais simples de implementar, mas menos responsivo e mais custoso; usado apenas como fallback caso o notification client tenha lacunas em algum cenário de hot-plug.

## 4. Persistência de configurações (trim/mute/solo)

**Decision**: Arquivo de configuração local em JSON (`%AppData%\AIRControl\channel-settings.json`), lido/escrito via `System.Text.Json`.

**Rationale**: Simples, sem dependência externa, atende FR-014 e SC-004 (restauração 100% confiável). Não há necessidade de um banco de dados para 2 canais com 3 propriedades cada.

**Alternatives considered**: Registro do Windows — mais integrado ao SO, mas menos portável/legível para debug; SQLite — over-engineering para este volume de dados.

## 5. Instância única do aplicativo

**Decision**: Named `Mutex` do Windows verificado no startup; se já existir, sinalizar e ativar a janela da instância existente via uma mensagem de janela customizada (`RegisterWindowMessage` + `PostMessage`) ou um named pipe simples.

**Rationale**: Atende FR-017 de forma padrão e confiável em apps WPF.

**Alternatives considered**: Lock de arquivo — funciona, mas não permite trazer a janela existente ao foco facilmente; named pipe sozinho — viável, mas mutex é mais simples para a checagem inicial de "já existe uma instância".

## 6. Testes automatizados

**Decision**: xUnit para testes unitários (lógica de trim/dB, resolução de conflito mute/solo, cálculo de peak/RMS) e testes de integração usando um `IAudioEngine`/`IAudioDeviceProvider` abstraído por interface, com uma implementação fake que simula buffers de áudio e eventos de conexão, permitindo testar sem hardware físico presente.

**Rationale**: Constituição exige testes unitários para lógica de negócio e testes de integração para qualquer coisa que cruze limites de componente (aqui, o limite é a camada de áudio/dispositivo). Abstrair a camada de áudio via interface permite CI sem depender do hardware AIR 192|4 real.

**Alternatives considered**: Testar apenas manualmente contra o hardware físico — rejeitado, pois viola o princípio de Testing Standards da constituição (testes automatizados são a evidência primária).

## 7. Acessibilidade (FR-020)

**Decision**: Usar controles WPF nativos (Slider para trim, ToggleButton para mute/solo, ProgressBar/custom control com `AutomationProperties` para os meters) com `AutomationProperties.Name`/`HelpText` e `LiveSetting` para anunciar mudanças de estado (mute/solo/clipping) a leitores de tela.

**Rationale**: Controles nativos do WPF já implementam `IAutomationPeer` corretamente; menos retrabalho de acessibilidade customizada. `AutomationProperties.LiveSetting=Polite` permite anunciar mudanças de estado sem exigir foco do usuário.

**Alternatives considered**: Controles totalmente customizados desenhados via `Canvas`/`DrawingContext` sem peers de automação — rejeitado por exigir reimplementação manual de toda a árvore de acessibilidade.

## Resumo de unknowns resolvidos

Nenhum item "NEEDS CLARIFICATION" permanece — a spec já continha uma seção de Clarifications que resolveu as ambiguidades de produto; as decisões acima resolvem as ambiguidades técnicas de implementação.
