<img src="docs/icone.png" width="96" alt="Ícone do Juicebar">

# Juicebar

A bateria dos seus aparelhos sem fio direto na barra de tarefas do Windows: fone, mouse, teclado, controle.

![Juicebar na barra de tarefas: fone com cada lado separado, mouse e teclado](docs/barra.png)

Fones com Fast Pair do Google (por exemplo, vários modelos da JBL) aparecem com cada lado separado, E (esquerdo) e D (direito). O Windows e a maioria dos programas só mostram um número para o fone inteiro.

## Funciona com

| Aparelho | Como a bateria é lida |
|---|---|
| Fones Bluetooth | pelo Windows; com Fast Pair, cada lado e o estojo separados |
| Mouse, teclado e controle Bluetooth | pelo Windows ou pelo serviço de bateria do Bluetooth LE |
| Logitech com receptor (Lightspeed, Unifying, Bolt) | pelo receptor, inclusive com o G HUB aberto |
| Razer com receptor (HyperSpeed) | pelo receptor |
| Redragon, Kysona, Pulsar, ATK/VXE com receptor Compx | pelo receptor |
| Qualquer marca que siga o padrão USB de bateria | pelo receptor |

Ainda não entram: Corsair, SteelSeries, HyperX e outras marcas com receptor próprio (cada modelo tem o seu protocolo e elas entram uma por vez), nem aparelhos com fio. Se o seu não aparece, veja [Aparelho não aparece?](#aparelho-não-aparece).

## Instalar

1. Baixe o `juicebar.exe` da [última versão](https://github.com/caiquebmaso/juicebar/releases/latest).
2. Coloque numa pasta fixa, por exemplo `%LOCALAPPDATA%\Juicebar`, e abra com dois cliques.
3. Para abrir sozinho com o Windows: clique direito no Juicebar na barra e marque **Iniciar com o Windows**.

O `.exe` não tem assinatura digital. Na primeira vez o Windows mostra a tela "O Windows protegeu o computador" (SmartScreen): clique em **Mais informações** e depois em **Executar assim mesmo**. Se preferir, [compile a partir do código](#compilar-a-partir-do-código).

**Requisitos:** Windows 10 com Bluetooth (testado no Windows 10 22H2). Usa o .NET Framework 4, que já vem no Windows; não precisa instalar mais nada. No Windows 11 a barra de tarefas é montada de outro jeito e o encaixe ainda não foi testado.

## Usar

O Juicebar fica na barra, à esquerda do clima e da área de notificação:

- cada aparelho conectado tem o seu ícone (fone, mouse, teclado, controle) e uma pilha com a porcentagem, um ao lado do outro;
- cores: branco acima de 40%, laranja até 40%, vermelho até 20%, verde carregando; o ícone pega a cor do nível mais baixo do aparelho;
- passar o mouse mostra todos os aparelhos, cada lado do fone, o estojo e a hora da leitura;
- sem nenhum aparelho com bateria conectado, fica só um ícone de Bluetooth apagado.

**Clique esquerdo** abre a janela de detalhes. **Clique direito** abre o menu:

| Opção | O que faz |
|---|---|
| Detalhes | janela com cada aparelho, de onde veio a leitura e os receptores sem leitura |
| Procurar atualizações | procura versão nova na hora (vira **Atualizar para a versão X** quando tem) |
| Copiar diagnóstico | copia o que o Juicebar achou, para mandar numa issue |
| Iniciar com o Windows | liga e desliga a abertura automática |
| Troquei as pilhas | só aparece com teclado Logitech a pilha; ver [Bateria por faixa](#bateria-por-faixa) |
| Sair | fecha o Juicebar |

### Bateria por faixa

Alguns aparelhos Logitech a pilha (como o teclado K270) não dão a porcentagem exata, só a faixa (100, 70, 20, 5...). Com a pilha fraca, a faixa muda com o uso: parado o teclado diz 70%, digitando cai para 20%, porque a tensão da pilha cede quando o rádio puxa corrente. Por isso o Juicebar mostra **a menor leitura das últimas 12 horas**, que é a que diz quando a pilha vai acabar.

Depois de trocar as pilhas, clique direito > **Troquei as pilhas**: ele esquece a leitura guardada e lê de novo na hora. Sem isso, o valor novo aparece sozinho depois de 12 horas.

## Atualizações

O Juicebar procura versão nova nos [releases](https://github.com/caiquebmaso/juicebar/releases) ao abrir e depois a cada 6 horas. Quando tem, aparece um **ponto amarelo** no canto do Juicebar e o clique direito mostra **Atualizar para a versão X**. Ao confirmar, ele baixa o `juicebar.exe` novo, troca pelo atual e abre de novo sozinho.

A versão 1.0.0 ainda não procurava atualizações: quem está nela precisa baixar a última versão uma vez.

## Desinstalar

1. Clique direito no Juicebar e desmarque **Iniciar com o Windows**.
2. Clique direito > **Sair**.
3. Apague a pasta onde está o `juicebar.exe` (por exemplo `%LOCALAPPDATA%\Juicebar`). Ali também fica o `faixas.txt` da bateria por faixa.

## Aparelho não aparece?

1. Com o aparelho ligado, clique direito no Juicebar > **Copiar diagnóstico**.
2. Abra uma issue [Meu aparelho não aparece](https://github.com/caiquebmaso/juicebar/issues/new?template=aparelho-nao-aparece.yml) e cole o texto.

O diagnóstico lista os aparelhos Bluetooth, os anúncios Fast Pair e os receptores USB que o Juicebar achou, e o que cada um respondeu. Receptores sem fio que ele reconhece mas ainda não sabe ler aparecem em **Sem leitura ainda**, na janela de detalhes. O [CONTRIBUTING.md](CONTRIBUTING.md) explica como gravar o que o programa da marca pergunta ao receptor, para a marca entrar no Juicebar.

## Privacidade

O Juicebar só lê: não conecta, não pareia e não grava nada nos aparelhos. Nos receptores ele só faz perguntas de leitura (versão, nome, bateria). Na internet, ele só acessa os releases deste repositório no GitHub para procurar atualização. Não coleta nem envia nenhum dado.

## Para quem programa

### Compilar a partir do código

1. Baixe ou clone o repositório.
2. Clique duas vezes em `juicebar.bat`. Na primeira vez ele compila o programa em `%LOCALAPPDATA%\Juicebar` com o compilador de C# que já vem no Windows (`csc.exe` do .NET Framework) e depois abre. Se o `juicebar.cs` mudar, ele compila de novo.

### Opções de linha de comando

| Opção | O que faz |
|---|---|
| `--demo` | mostra fone, mouse e teclado de exemplo, sem ler nada (como na imagem acima) |
| `--testar arquivo.txt` | confere a leitura de cada protocolo com respostas gravadas (Logitech, Razer, Compx, padrão USB, Fast Pair) e a comparação de versões; o código de saída é o número de falhas |
| `--diagnostico arquivo.txt` | grava o diagnóstico dos aparelhos USB e sai |

### De onde vem a bateria

1. **Windows:** a bateria que o Windows já recebe de qualquer aparelho Bluetooth conectado (a mesma de Configurações > Bluetooth), agrupada por aparelho. Lida a cada 20 s.
2. **Bluetooth LE:** aparelho LE conectado sem bateria no Windows; lê o serviço padrão de bateria (0x180F / 0x2A19) a cada 2 min.
3. **Fast Pair:** fones que anunciam a bateria de cada lado e do estojo (serviço 0xFE2C). Só vale para fone conectado a este PC, casado pelo nome, para que o fone de outra pessoa por perto não apareça.
4. **USB e receptores 2.4 GHz** (a cada 1 min), em ordem:
   - **padrão USB de bateria** (HID Battery Strength);
   - **Logitech HID++** (Lightspeed, Unifying, Bolt e aparelhos Logitech por cabo). No Unifying, aparelho que não informa o próprio nome (ex.: K270) aparece com o nome e o tipo que o receptor guardou no pareamento;
   - **Razer** (relatório de 90 bytes da marca);
   - **Compx** (VID 3554), pelo mesmo protocolo do driver `hid-pulsar` do Linux. Mouse dormindo não responde: fica a última leitura por 5 min.

   Cada aparelho USB tem prazo para responder; o que travar é pulado por 10 min e aparece como "TRAVOU" no diagnóstico, sem segurar a barra.

### Como ele fica na barra

O programa cria uma janela filha da barra de tarefas (`Shell_TrayWnd`), desenhada com transparência (`UpdateLayeredWindow`), e encolhe a área dos botões (`ReBarWindow32`) para abrir espaço. Se o Explorer reiniciar, ele se encaixa de novo sozinho.

### Ícone

O `juicebar.ico` é gerado pelo `tools\gerar-icone.ps1` (cada tamanho desenhado do zero).

## Contribuir

Veja o [CONTRIBUTING.md](CONTRIBUTING.md): dá para ajudar só mandando o diagnóstico, ou mandando o código por pull request.

## Licença

[MIT](LICENSE)
