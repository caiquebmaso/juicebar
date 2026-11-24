<img src="docs/icone.png" width="96" alt="Ícone do Juicebar">

# Juicebar

A bateria dos seus aparelhos sem fio direto na barra de tarefas do Windows: fone, mouse, teclado, controle.

![Juicebar na barra de tarefas: fone com cada lado separado, mouse e teclado](docs/barra.png)

Fones com Fast Pair do Google (por exemplo, vários modelos da JBL) aparecem com cada lado separado, E (esquerdo) e D (direito). O Windows e a maioria dos programas só mostram um número para o fone inteiro.

## Instalar

1. Baixe o `juicebar.exe` da [última versão](https://github.com/caiquebmaso/juicebar/releases/latest).
2. Coloque num lugar fixo, por exemplo `%LOCALAPPDATA%\Juicebar`, e abra com dois cliques.
3. Para abrir sozinho com o Windows: clique direito no Juicebar na barra e marque **Iniciar com o Windows**.

O `.exe` não tem assinatura digital. Na primeira vez o Windows mostra a tela "O Windows protegeu o computador" (SmartScreen): clique em **Mais informações** e depois em **Executar assim mesmo**. Se preferir, compile você mesmo a partir do código (abaixo).

Não precisa instalar nada além disso: o programa usa o .NET Framework 4, que já vem no Windows.

### Compilar a partir do código

1. Baixe ou clone o repositório.
2. Clique duas vezes em `juicebar.bat`. Na primeira vez ele compila o programa em `%LOCALAPPDATA%\Juicebar` com o compilador de C# que já vem no Windows e depois abre. Se o `.cs` mudar, ele compila de novo.

## Como usar

Na barra, à esquerda do clima e da área de notificação:

- cada aparelho conectado tem o seu ícone (fone, mouse, teclado, controle) e uma pilha com a porcentagem, um ao lado do outro;
- cores: branco acima de 40%, laranja até 40%, vermelho até 20%, verde carregando; o ícone pega a cor do nível mais baixo do aparelho;
- passar o mouse mostra todos os aparelhos, cada lado do fone, o estojo e a hora da leitura;
- clique esquerdo abre a janela de detalhes; clique direito tem Detalhes, Procurar atualizações, Copiar diagnóstico, Iniciar com o Windows, **Troquei as pilhas** (só aparece com aparelho que informa a bateria por faixa, ver abaixo) e Sair;
- sem nenhum aparelho com bateria conectado, fica só um ícone de Bluetooth apagado.

### Atualizações

O Juicebar procura versão nova nos releases deste repositório ao abrir e depois a cada 6 horas. Quando tem, aparece um **ponto amarelo** no canto do Juicebar na barra, e o clique direito mostra **Atualizar para a versão X**. Ao confirmar, ele baixa o `juicebar.exe` novo, troca pelo atual e abre de novo sozinho. Também dá para procurar na hora pelo clique direito, em **Procurar atualizações**.

Quem está numa versão estável só recebe versão estável; quem está numa beta recebe as betas também.

### Diagnóstico

Clique direito > **Copiar diagnóstico** copia uma lista de tudo o que o Juicebar achou: aparelhos Bluetooth, anúncios Fast Pair, receptores USB e o que cada um respondeu. Se um aparelho sem fio não aparece na barra, abra uma [issue](https://github.com/caiquebmaso/juicebar/issues) e cole esse texto. Receptores sem fio que ele reconhece mas ainda não sabe ler aparecem em **Sem leitura ainda**, na janela de detalhes.

Para ver o visual sem aparelho nenhum: `juicebar.bat --demo` (fone, mouse e teclado de exemplo, como na imagem acima).

### Abrir com o Windows

Clique direito no Juicebar na barra e marque **Iniciar com o Windows**. Desmarque para tirar.

### Ícone

O `juicebar.ico` é gerado pelo `tools\gerar-icone.ps1` (cada tamanho desenhado do zero).

## De onde vem a bateria

1. **Windows:** a bateria que o Windows já recebe de qualquer aparelho Bluetooth conectado (a mesma de Configurações > Bluetooth). Lida a cada 20 s.
2. **Bluetooth LE:** aparelho LE conectado sem bateria no Windows. O programa lê o serviço padrão de bateria (0x180F / 0x2A19) a cada 2 min.
3. **Fast Pair:** fones que anunciam a bateria de cada lado e do estojo (serviço 0xFE2C). Só vale para fone conectado a este PC, casado pelo nome, para que o fone de outra pessoa por perto não apareça.

4. **USB e receptores 2.4 GHz** (a cada 1 min). No 2.4 GHz não existe um padrão único: cada marca passa a bateria pelo receptor do seu jeito. O Juicebar tenta, em ordem:
   - o **padrão USB de bateria** (HID, Battery Strength), que vale para qualquer marca que o siga;
   - **Logitech** (Lightspeed, Unifying, Bolt e aparelhos Logitech por cabo), pelo protocolo HID++, inclusive com o G HUB aberto;
   - **Razer** (receptores HyperSpeed e aparelhos sem fio da marca), pelo protocolo de relatórios da Razer;
   - **Compx** (receptores VID 3554, como os dos mouses Redragon K1NG M916 Pro, Kysona, Pulsar e ATK/VXE), pelo mesmo protocolo do driver `hid-pulsar` do Linux. Mouse dormindo não responde: fica a última leitura por 5 min.

   No Logitech Unifying, aparelho que não informa o próprio nome (ex.: teclado K270) aparece com o nome e o tipo que o receptor guardou no pareamento.

### Bateria por faixa (teclados Logitech a pilha, como o K270)

Alguns aparelhos Logitech não dão a porcentagem exata, só a faixa (100, 70, 20, 5...). Com a pilha fraca, a faixa muda com o uso: parado o teclado diz 70%, digitando cai para 20%, porque a tensão da pilha cede quando o rádio puxa corrente. Por isso o Juicebar mostra **a menor leitura das últimas 12 horas**, que é a que diz quando a pilha vai acabar. Essa leitura fica gravada em `%LOCALAPPDATA%\Juicebar\faixas.txt` e continua valendo depois de reiniciar.

Depois de trocar as pilhas, clique direito > **Troquei as pilhas (zerar menor leitura)**: ele esquece a leitura guardada e lê os receptores na hora. Sem isso, o valor novo aparece sozinho depois de 12 horas sem leitura baixa.

Tudo é só leitura: o programa não conecta, não pareia e não grava nada no aparelho. Nos receptores ele só faz perguntas de leitura (versão, nome, bateria).

## O que ainda não entra

- Corsair, SteelSeries, HyperX e outras marcas com receptor próprio: cada modelo tem o seu protocolo e elas vão entrar uma por vez. Se o seu não aparece, mande o diagnóstico numa issue.
- Aparelhos com fio.

## Testes

`juicebar.exe --testar arquivo.txt` confere a leitura de cada protocolo com respostas montadas byte a byte pela documentação (Logitech, Razer, Compx, padrão USB, Fast Pair) e a comparação de versões. O código de saída é o número de falhas.

`juicebar.exe --diagnostico arquivo.txt` grava o diagnóstico dos aparelhos USB e sai.

## Como ele fica na barra

O programa cria uma janela filha da barra de tarefas (`Shell_TrayWnd`), desenhada com transparência (`UpdateLayeredWindow`), e encolhe a área dos botões (`ReBarWindow32`) para abrir espaço. Se o Explorer reiniciar, ele se encaixa de novo sozinho.

## Requisitos

Windows 10 ou 11, com Bluetooth e .NET Framework 4.x (já vem no Windows).

## Contribuir

Tem um aparelho sem fio que não aparece? Veja o [CONTRIBUTING.md](CONTRIBUTING.md): dá para ajudar só mandando o diagnóstico, ou mandando o código por pull request.

## Licença

[MIT](LICENSE)
