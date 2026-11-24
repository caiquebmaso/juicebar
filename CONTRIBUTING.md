# Como contribuir

O jeito mais útil de ajudar é fazer o Juicebar ler a bateria de um aparelho que ele ainda não lê. No 2.4 GHz cada marca (às vezes cada modelo) passa a bateria do seu jeito, então cada aparelho novo precisa de alguém que tenha ele em mãos.

## 1. Mandar o diagnóstico (sem programar)

1. Baixe o `juicebar.exe` da versão mais recente na página de [releases](https://github.com/caiquebmaso/juicebar/releases/latest).
2. Plugue o receptor, ligue o aparelho e abra o Juicebar.
3. Clique direito no Juicebar na barra > **Copiar diagnóstico**.
4. Abra uma [issue "Meu aparelho não aparece"](https://github.com/caiquebmaso/juicebar/issues/new?template=aparelho-nao-aparece.yml) e cole o texto.

O diagnóstico lista os aparelhos e receptores que o Juicebar achou e o que cada um respondeu. Ele não tem senha nem dado pessoal: só nomes de aparelho, códigos USB e porcentagens.

## 2. Gravar como o programa da marca pergunta a bateria

Se o aparelho não aparecer nem pelo padrão USB, a forma de descobrir o protocolo é gravar a conversa entre o programa da marca e o receptor.

1. Instale o programa da marca que mostra a bateria do aparelho.
2. Instale o [Wireshark](https://www.wireshark.org/) e marque o **USBPcap** durante a instalação. Depois reinicie o PC.
3. No Wireshark, comece a gravar na interface USBPcap onde o receptor está plugado.
4. Abra a tela de bateria no programa da marca e espere uns 10 segundos.
5. Pare a gravação, salve o arquivo (`.pcapng`) e anexe na issue. Anote também a porcentagem que o programa mostrou e se o aparelho estava carregando.

A gravação mostra só o tráfego USB. Mesmo assim, evite digitar senhas enquanto ela estiver rodando.

## 3. Mandar o código

1. Faça um **Fork** deste repositório (botão no topo da página) e clone o seu fork.
2. Crie um branch, por exemplo `marca-redragon`.
3. Mude o `juicebar.cs`. Cada marca fica na fonte 4 (`Receptores.Ler`), no mesmo formato da Logitech e da Razer:
   - a leitura fica numa classe própria, só com perguntas de leitura (nunca grave configuração no aparelho);
   - a decodificação da resposta fica separada, para poder testar sem o aparelho;
   - acrescente em `Testes.Rodar` pelo menos um teste com uma resposta real que você gravou.
4. Teste:
   - `juicebar.bat` compila e abre (precisa só do Windows, nada para instalar);
   - `%LOCALAPPDATA%\Juicebar\juicebar.exe --testar resultado.txt` tem de terminar com `TUDO OK`;
   - `juicebar.exe --diagnostico diag.txt` deve mostrar a bateria do seu aparelho;
   - confira na barra que a porcentagem bate com a do programa da marca.
5. Faça commit, envie para o seu fork e abra um **pull request** para o `main` deste repositório. Descreva o aparelho (marca, modelo, código USB) e cole o diagnóstico de antes e de depois.

O pull request só entra depois de revisado e aceito pelo dono do repositório.

## Regras do código

- C# 5, compilado pelo `csc.exe` do .NET Framework que já vem no Windows: sem `async`/`await` nem interpolação de string (`$"..."`), sem pacotes externos.
- Um arquivo só (`juicebar.cs`).
- Só leitura: o Juicebar não conecta, não pareia e não grava nada nos aparelhos.
- Código de outros projetos com licença GPL (Solaar, OpenRazer, HeadsetControl e afins) não pode ser copiado, porque este projeto é MIT. Pode usar a documentação do protocolo e escrever do seu jeito.
