# Feature Specification: Audio Stability & Consistency Fixes

**Feature Branch**: `004-fix-audio-stability`

**Created**: 2026-09-03

**Status**: Draft

**Input**: User description: "o desenvolvimento até aqui está apresentando algumas falhas e bugs que estão bem inconsistentes. esta feature será para investigar o que há de errado (tanto do que eu vou falar a seguir quanto o que mais for encontrado). / Por vezes ao abrir o app, o campo 'Modo de roteamento' não apresenta as opções. / Em 'Formato de gravação (Windows)' ele tem funcionado bem consistente, mas ele só deveria aparecer as opções de sample rate / bits de acordo com a configuração do campo 'Sample rate do driver (ASIO)'. / Por vezes a monitoração dos inputs congela, para de funcionar, etc. / Precisamos ter certeza que as configurações, ordem em que cada processo abre e roda, tudo está correto. / Investigar a fundo as possibilidades que as funcionalidades e bibliotecas que estamos usando nos fornecem. Verificar se há algo melhor para ser usado ou melhor configurado."

## Clarifications

### Session 2026-09-03

- Q: A revisão de tecnologia (User Story 5) entrega apenas o documento de recomendação, ou também implementa as mudanças recomendadas nesta mesma feature? → A: Documento **e** implementação completa, incluindo a troca da tecnologia de captura caso a revisão a recomende. A revisão continua sendo entregue como documento revisável antes de qualquer implementação começar.
- Q: O filtro do formato de gravação pelo sample rate do driver pode consultar o driver em tempo real, ou deve usar um valor em cache? → A: Consulta em tempo real, dentro de uma janela segura — a captura é parada, o driver é consultado, e a captura é retomada. Essa interrupção é um estado explicitamente permitido, mas limitado: só pode ser disparada por eventos discretos (nunca por polling contínuo), tem teto de duração, é sinalizada ao usuário e sempre termina com a captura restabelecida.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - O app inicializa corretamente em toda abertura (Priority: P1)

Como usuário, quero que toda vez que eu abrir o AIR Control o app chegue ao mesmo estado funcional: dispositivo ativo resolvido, medidores se movendo com o sinal real, e todos os campos de configuração preenchidos com as opções corretas — sem depender de sorte, de ordem de conexão do hardware, ou de reabrir o app.

Hoje a inicialização é intermitente: às vezes o campo "Modo de roteamento" abre vazio (sem nenhuma opção para escolher), o que deixa o usuário sem conseguir configurar o roteamento até fechar e reabrir o app.

**Why this priority**: É o primeiro contato do usuário com o app em toda sessão. Um startup que falha de forma aleatória bloqueia todas as outras funcionalidades e destrói a confiança no produto — o usuário não consegue distinguir "o app quebrou" de "meu hardware quebrou".

**Independent Test**: Abrir e fechar o app repetidamente (mínimo 20 ciclos) sob condições variadas — dispositivo já conectado, dispositivo conectado depois do app abrir, dispositivo desconectado, outro app usando o dispositivo — e confirmar que em 100% dos ciclos o seletor de modo de roteamento apresenta pelo menos uma opção válida e a seleção persistida é restaurada.

**Acceptance Scenarios**:

1. **Given** o dispositivo de entrada está conectado e ocioso, **When** o usuário abre o app, **Then** o campo "Modo de roteamento" apresenta todas as opções compatíveis com a contagem de canais do dispositivo ativo e a opção salva na sessão anterior aparece selecionada.
2. **Given** o app foi aberto sem o dispositivo conectado, **When** o usuário conecta o dispositivo com o app já aberto, **Then** o campo "Modo de roteamento" se popula sozinho com as opções válidas, sem exigir reinício do app.
3. **Given** o app não conseguiu determinar a contagem de canais do dispositivo, **When** a tela é exibida, **Then** o campo mostra uma mensagem acionável explicando o que aconteceu e o que fazer, nunca uma lista vazia sem explicação.
4. **Given** o usuário abre o app 20 vezes seguidas nas mesmas condições, **When** cada instância termina de inicializar, **Then** todas as 20 chegam ao mesmo estado de configuração exibida.

---

### User Story 2 - Monitoração e metering nunca congelam (Priority: P1)

Como usuário, quero que a monitoração e os medidores de entrada continuem funcionando de forma ininterrupta durante toda a sessão, e que se algo interromper o fluxo de áudio o app se recupere sozinho ou me diga claramente o que aconteceu — em vez de simplesmente congelar sem aviso.

Hoje a monitoração às vezes congela: os medidores param de se mover e/ou o áudio para de sair, sem nenhuma mensagem, e a única saída é reiniciar o app.

**Why this priority**: Metering e monitoração são a função central do produto. Um congelamento silencioso é a pior falha possível aqui, porque durante uma gravação o usuário pode confiar em um medidor parado e perder material. Empata em prioridade com a inicialização.

**Independent Test**: Manter o app rodando com sinal ao vivo por uma sessão longa (mínimo 60 minutos) enquanto se provocam perturbações reais — trocar configurações no app, abrir/fechar o painel do fabricante, iniciar outro aplicativo de áudio, suspender e retomar a máquina, desconectar e reconectar o dispositivo — e confirmar que os medidores voltam a refletir o sinal em todos os casos, ou exibem um estado de erro explicativo.

**Acceptance Scenarios**:

1. **Given** o app está monitorando um sinal ao vivo, **When** o usuário altera qualquer configuração de dispositivo (formato de gravação, sample rate do driver, modo de roteamento, dispositivo ativo), **Then** os medidores voltam a se mover com o sinal em poucos segundos sem intervenção manual.
2. **Given** o app está monitorando, **When** o fluxo de áudio é interrompido por causa externa (dispositivo removido, driver reiniciado, máquina retomada de suspensão, outro aplicativo tomou o dispositivo em modo exclusivo), **Then** o app detecta a interrupção e ou retoma a monitoração automaticamente, ou exibe uma mensagem que diz o que aconteceu e o que o usuário pode fazer.
3. **Given** o app rodou por uma sessão longa, **When** o usuário verifica os medidores a qualquer momento, **Then** eles refletem o sinal atual — nunca um valor congelado de um instante anterior.
4. **Given** a monitoração está desabilitada ou o canal está mutado/soloado fora, **When** há sinal na entrada, **Then** os medidores continuam se movendo (comportamento já estabelecido pela feature 003 e que não pode regredir).

---

### User Story 3 - Opções de formato de gravação seguem o sample rate do driver (Priority: P2)

Como usuário, quero que o campo "Formato de gravação (Windows)" só ofereça combinações de sample rate/bit depth que correspondam ao valor configurado em "Sample rate do driver (ASIO)", para que eu não consiga escolher, por engano, um formato que coloca o Windows e o driver em desacordo.

**Why this priority**: Não é uma falha de disponibilidade — o campo funciona de forma consistente hoje. É uma correção de coerência que previne um erro de configuração que o usuário só descobriria depois, ao ouvir um problema. Vem depois das falhas que impedem o uso do app.

**Independent Test**: Configurar o sample rate do driver em cada valor suportado e, em cada caso, abrir o campo de formato de gravação e confirmar que apenas as combinações com aquele sample rate são oferecidas — e que, com a monitoração ativa, a pausa provocada pela consulta respeita o teto de duração e a monitoração volta sozinha.

**Acceptance Scenarios**:

1. **Given** o sample rate do driver está em um valor específico, **When** o usuário abre a lista de formatos de gravação, **Then** apenas as opções de bit depth com aquele sample rate são exibidas.
2. **Given** o usuário altera o sample rate do driver, **When** a alteração é aplicada, **Then** a lista de formatos de gravação se atualiza imediatamente para o novo sample rate, sem exigir reinício do app.
3. **Given** o formato de gravação atualmente aplicado tem um sample rate diferente do novo valor do driver, **When** o sample rate do driver muda, **Then** o app reconcilia os dois lados e informa ao usuário qual formato passou a valer.
4. **Given** o app não consegue determinar o sample rate atual do driver, **When** a lista de formatos é exibida, **Then** o app deixa isso explícito ao usuário em vez de filtrar silenciosamente pelo valor errado.
5. **Given** o app está monitorando um sinal ao vivo, **When** o app precisa consultar o driver para montar a lista de formatos, **Then** a monitoração é pausada e retomada dentro do teto de duração definido, o usuário vê um estado transitório indicando a reconfiguração, e ao final os medidores voltam a se mover com o sinal.
6. **Given** o app está monitorando, **When** o usuário não realiza nenhuma ação relacionada a formato ou driver, **Then** o app nunca consulta o driver por conta própria e a monitoração não sofre nenhuma pausa.

---

### User Story 4 - Levantamento documentado da ordem de inicialização (Priority: P2)

Como responsável pelo projeto, quero um levantamento escrito de como o app inicializa hoje — que passo roda em que ordem, que estado cada um depende, e onde estão as condições de corrida — junto com as correções aplicadas, para que eu possa confiar que os bugs intermitentes foram resolvidos na causa raiz e não apenas mascarados.

**Why this priority**: Os sintomas relatados (campo vazio, medidor congelado) são intermitentes, o que é a assinatura de um problema de ordem/temporização e não de lógica isolada. Sem esse levantamento, cada correção pontual corre o risco de mover o problema em vez de eliminá-lo.

**Independent Test**: Ler o documento produzido e conseguir, sem abrir o código, explicar a ordem de inicialização, apontar cada falha intermitente relatada à sua causa raiz identificada, e verificar que existe um teste automatizado correspondente a cada causa.

**Acceptance Scenarios**:

1. **Given** a investigação foi concluída, **When** o documento é revisado, **Then** ele descreve a sequência de inicialização completa e identifica cada ponto onde a ordem de execução importa.
2. **Given** um sintoma relatado pelo usuário, **When** se consulta o documento, **Then** existe uma causa raiz identificada para ele, ou uma declaração explícita de que a causa não foi reproduzida e o que foi feito para monitorá-la.
3. **Given** uma causa raiz foi corrigida, **When** a suíte de testes roda, **Then** existe um teste de regressão que falha sem a correção e passa com ela.

---

### User Story 5 - Revisão de tecnologia, decidida e aplicada (Priority: P3)

Como responsável pelo projeto, quero uma avaliação das bibliotecas e APIs de áudio que o app usa hoje — o que elas oferecem que ainda não estamos aproveitando, onde estamos usando-as de forma subótima, e se existe alternativa melhor — entregue primeiro como recomendação revisável e, uma vez aprovada por mim, **aplicada ao app dentro desta mesma feature**, inclusive uma eventual troca da tecnologia de captura.

**Why this priority**: É trabalho de fundação, mas com implementação incluída ele muda a base sobre a qual as correções de estabilidade rodam. Por isso vem depois: as correções das histórias P1 devem estar entregues e verdes antes que qualquer mudança estrutural seja aplicada, para que se saiba com certeza qual bug foi corrigido e qual foi apenas deslocado pela nova base.

**Independent Test**: Ler o documento de recomendação e tomar uma decisão de adotar/não adotar para cada item sem investigação adicional; depois, com as mudanças aprovadas aplicadas, rodar a suíte completa e a validação manual de estabilidade e confirmar que nenhum comportamento das histórias P1 regrediu.

**Acceptance Scenarios**:

1. **Given** a revisão foi concluída, **When** o documento é lido, **Then** cada capacidade relevante hoje não aproveitada aparece com o benefício concreto que traria e o esforço estimado.
2. **Given** uma alternativa de biblioteca ou API é proposta, **When** o documento é lido, **Then** ele apresenta prós, contras, risco de migração e uma recomendação clara de adotar ou não.
3. **Given** a configuração atual de alguma biblioteca é subótima, **When** o documento é lido, **Then** ele indica o ajuste recomendado e como medir a melhoria.
4. **Given** o responsável aprovou um conjunto de recomendações, **When** a implementação é concluída, **Then** cada item aprovado está aplicado no app e cada item não aprovado permanece sem alteração.
5. **Given** uma mudança estrutural foi aplicada (incluindo troca da tecnologia de captura), **When** a suíte de testes e a validação manual de estabilidade rodam, **Then** todos os critérios das User Stories 1 e 2 continuam sendo atendidos e a melhoria prometida é demonstrada por medição.
6. **Given** uma mudança aplicada não entrega a melhoria prometida ou introduz regressão, **When** isso é constatado na validação, **Then** a mudança é revertida e o motivo é registrado no documento.

---

### Edge Cases

- O que acontece quando o dispositivo é conectado exatamente durante a janela de inicialização do app (corrida entre o startup e o evento de conexão)?
- O que acontece quando duas instâncias do app são abertas ao mesmo tempo?
- O que acontece quando outro aplicativo já detém o dispositivo em modo exclusivo no momento em que o app inicia?
- O que acontece quando o usuário altera o sample rate pelo painel do fabricante (fora do app) enquanto o app está monitorando?
- O que acontece quando a máquina entra em suspensão e retoma com o app aberto e monitorando?
- O que acontece quando uma configuração persistida da sessão anterior aponta para um dispositivo que não existe mais, ou para um valor que o dispositivo atual não suporta?
- O que acontece quando o usuário altera várias configurações em sequência rápida, antes que a anterior tenha terminado de ser aplicada?
- O que acontece quando o dispositivo relata zero canais de entrada durante uma janela transitória de reconexão?

## Requirements *(mandatory)*

### Functional Requirements

**Inicialização determinística**

- **FR-001**: O app MUST chegar ao mesmo estado de configuração exibida em toda inicialização feita sob as mesmas condições de hardware, sem variação entre execuções.
- **FR-002**: O seletor de modo de roteamento MUST apresentar todas as opções compatíveis com o dispositivo ativo sempre que houver um dispositivo ativo com contagem de canais conhecida.
- **FR-003**: Quando qualquer campo de configuração dependente do dispositivo não puder ser populado, o app MUST exibir uma mensagem que diz o que aconteceu e o que o usuário pode fazer, em vez de um campo vazio sem explicação.
- **FR-004**: O app MUST popular ou repopular os campos dependentes do dispositivo automaticamente quando um dispositivo válido passa a estar disponível depois da inicialização, sem exigir reinício.
- **FR-005**: Uma falha em qualquer etapa da inicialização MUST NOT impedir que as demais etapas independentes concluam.

**Continuidade da monitoração e metering**

- **FR-006**: A monitoração e o metering MUST continuar refletindo o sinal de entrada de forma ininterrupta durante toda a sessão, salvo interrupção externa detectada e comunicada, ou uma pausa deliberada de reconfiguração conforme FR-015a a FR-015d.
- **FR-007**: O app MUST detectar quando o fluxo de áudio parou de entregar dados e MUST reagir — retomando automaticamente ou exibindo um estado de erro explicativo — em vez de permanecer com medidores congelados.
- **FR-008**: Qualquer alteração de configuração feita pelo usuário MUST deixar a monitoração operacional ao final, sem intervenção manual adicional.
- **FR-009**: O app MUST se recuperar de desconexão e reconexão do dispositivo, de retomada de suspensão da máquina, e da perda do dispositivo para outro aplicativo, retomando a monitoração quando o dispositivo voltar a estar disponível.
- **FR-010**: Os medidores MUST continuar medindo independentemente do estado de monitoração, mute e solo (comportamento existente que não pode regredir).

**Coerência entre formato do Windows e sample rate do driver**

- **FR-011**: O campo de formato de gravação do Windows MUST oferecer apenas combinações cujo sample rate corresponda ao sample rate configurado no driver.
- **FR-012**: Quando o sample rate do driver mudar, o app MUST atualizar imediatamente as opções de formato de gravação oferecidas.
- **FR-013**: Quando o formato de gravação em vigor deixar de corresponder ao sample rate do driver, o app MUST reconciliar os dois lados e MUST informar ao usuário qual formato passou a valer.
- **FR-014**: Quando o sample rate do driver não puder ser determinado, o app MUST tornar essa condição visível ao usuário em vez de filtrar as opções por um valor presumido.
- **FR-015**: O app MUST consultar o sample rate corrente diretamente do driver — nunca um valor presumido — a cada vez que precisar montar ou revalidar a lista de formatos de gravação.
- **FR-015a**: Quando a captura estiver ativa, a consulta ao driver MUST ocorrer dentro de uma janela controlada: a captura é parada, o driver é consultado, e a captura é restabelecida ao final — em todos os caminhos, inclusive quando a consulta falha.
- **FR-015b**: Essa pausa de reconfiguração MUST ser disparada apenas por eventos discretos originados de uma ação do usuário ou de uma mudança real de dispositivo (abrir a lista de formatos, alterar o sample rate do driver, trocar o dispositivo ativo, inicializar). O app MUST NOT consultar o driver de forma periódica ou especulativa.
- **FR-015c**: A pausa de reconfiguração MUST ser sinalizada ao usuário por um estado transitório visível enquanto dura, para que uma pausa deliberada nunca seja confundida com o congelamento descrito na User Story 2.
- **FR-015d**: A pausa de reconfiguração MUST respeitar um teto de duração definido; se a captura não for restabelecida dentro desse teto, o app MUST tratar isso como falha e exibir um estado de erro acionável em vez de permanecer pausado silenciosamente.

**Investigação e verificação**

- **FR-016**: A investigação MUST produzir um documento que descreve a sequência de inicialização, as dependências entre etapas e cada ponto onde a ordem de execução importa.
- **FR-017**: A investigação MUST rastrear cada sintoma relatado até uma causa raiz identificada, ou declarar explicitamente que a causa não foi reproduzida e como ela será monitorada.
- **FR-018**: A investigação MUST reportar também as falhas encontradas que não foram relatadas pelo usuário, classificadas por severidade.
- **FR-019**: Cada causa raiz corrigida MUST ter um teste automatizado de regressão que falha sem a correção e passa com ela.
- **FR-020**: A revisão de tecnologia MUST produzir, para cada capacidade não aproveitada, configuração subótima ou alternativa considerada, uma recomendação com benefício, custo e risco.
- **FR-020a**: As recomendações MUST ser submetidas à aprovação do responsável antes de qualquer implementação; o app MUST receber apenas as mudanças aprovadas.
- **FR-020b**: A implementação das recomendações aprovadas — incluindo uma eventual substituição da tecnologia de captura — MUST ocorrer somente depois que as correções das User Stories 1 e 2 estiverem entregues e com a suíte verde, para que a origem de qualquer regressão permaneça atribuível.
- **FR-020c**: Cada mudança aplicada MUST ter sua melhoria prometida demonstrada por medição comparável antes e depois.
- **FR-020d**: Uma mudança aplicada que não entregue a melhoria prometida ou que introduza regressão MUST ser revertida, com o motivo registrado.
- **FR-021**: Nenhuma correção ou mudança desta feature MUST regredir os comportamentos já entregues pelas features 001, 002 e 003; a suíte existente MUST permanecer verde.

### Key Entities

- **Sequência de inicialização**: A ordem em que o app resolve dispositivo ativo, aplica formato, inicia captura e popula os campos de configuração; cada etapa tem pré-condições e um comportamento definido em caso de falha.
- **Estado do dispositivo ativo**: Identidade, contagem de canais, formatos suportados e sample rate corrente do dispositivo em uso; é a fonte de verdade da qual os campos de configuração derivam suas opções.
- **Estado de saúde do fluxo de áudio**: Se o áudio está entregando dados, parado, ou em erro; e há quanto tempo o último dado chegou.
- **Achado de investigação**: Um sintoma, sua causa raiz identificada (ou o registro de não reprodução), a correção aplicada e o teste de regressão correspondente.
- **Pausa de reconfiguração**: Uma interrupção deliberada e limitada da captura, motivada por um evento discreto, com início e fim visíveis ao usuário e teto de duração; distinta de um congelamento, que é involuntário e sem fim previsto.
- **Recomendação de tecnologia**: Um item da revisão com benefício esperado, custo, risco, decisão de aprovação do responsável, medição antes/depois e resultado final (aplicada ou revertida).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Em 20 aberturas consecutivas do app sob condições equivalentes, 20 apresentam todos os campos de configuração populados corretamente — zero ocorrências de campo de roteamento vazio.
- **SC-002**: Em uma sessão contínua de 60 minutos com sinal ao vivo e perturbações provocadas, os medidores nunca ficam congelados por mais de 5 segundos sem que o app exiba um estado de erro explicativo.
- **SC-003**: Após qualquer alteração de configuração feita pelo usuário, a monitoração volta a funcionar em até 3 segundos sem nenhuma ação adicional, em 100% das tentativas.
- **SC-004**: Para cada sample rate suportado pelo driver, as opções de formato de gravação oferecidas correspondem exatamente às combinações daquele sample rate — zero opções incompatíveis oferecidas.
- **SC-004a**: Toda pausa de reconfiguração provocada pela consulta ao driver dura no máximo 2 segundos e termina com a monitoração restabelecida, em 100% das ocorrências; o usuário vê um indicador durante toda a pausa.
- **SC-004b**: Durante 30 minutos de operação sem que o usuário toque em nenhum controle de formato ou de driver, ocorrem zero pausas de reconfiguração.
- **SC-005**: 100% dos sintomas relatados pelo usuário têm uma causa raiz documentada ou um registro explícito de não reprodução com plano de monitoramento.
- **SC-006**: 100% das causas raiz corrigidas têm um teste automatizado de regressão que falha antes da correção.
- **SC-007**: Nenhum cenário de aceitação das features 001, 002 e 003 regride — a suíte de testes existente permanece integralmente verde.
- **SC-008**: A revisão de tecnologia permite decidir adotar/não adotar cada item proposto sem investigação adicional, confirmado em uma leitura de revisão.
- **SC-009**: 100% das mudanças de tecnologia aplicadas foram previamente aprovadas pelo responsável, e 100% delas têm medição antes/depois registrada.
- **SC-010**: Depois de aplicadas todas as mudanças aprovadas, os critérios SC-001, SC-002 e SC-003 continuam sendo atendidos na revalidação com hardware real.

## Assumptions

- O hardware de referência para validação é o mesmo M-Audio AIR 192|4 usado nas features anteriores; cenários que exigem outro hardware ficam fora do escopo desta feature.
- "Sample rate do driver (ASIO)" é a fonte de verdade para o sample rate; o formato do Windows é o lado que se ajusta a ele, e não o contrário — comportamento já estabelecido na feature 003.
- Os sintomas intermitentes têm causa em ordem de execução e temporização entre inicialização, negociação de formato e início da captura; a investigação deve confirmar ou refutar essa hipótese, não assumi-la.
- A validação de estabilidade exige teste manual com hardware real além da suíte automatizada, porque congelamentos intermitentes não são integralmente reproduzíveis com dispositivos simulados.
- O escopo da revisão de tecnologia (User Story 5) é a camada de áudio e a inicialização; não inclui reavaliar o framework de interface.
- As preferências persistidas de sessões anteriores permanecem no formato atual; nenhuma migração de dados do usuário é necessária.
- O teto de 2 segundos para a pausa de reconfiguração (SC-004a) é um valor inicial derivado do que um usuário tolera como transição visível; se a medição com hardware real mostrar que o driver não responde nesse prazo, o teto é reavaliado com o responsável em vez de simplesmente removido.
- A troca da tecnologia de captura (User Story 5) só ocorre se a revisão a recomendar e o responsável a aprovar; a spec não pressupõe que ela vá acontecer.
- A aprovação das recomendações de tecnologia é dada pelo responsável do projeto, que é o mesmo usuário do app neste contexto.
