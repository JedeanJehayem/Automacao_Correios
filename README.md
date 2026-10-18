# Automação de rastreamento dos Correios

Aplicação RPA em C# que consulta códigos de rastreamento no portal dos Correios, resolve o CAPTCHA com OCR, abre o histórico completo e converte os eventos encontrados no DOM para arquivos JSON estruturados.

O fluxo é totalmente automático. A própria planilha de entrada guarda o controle: uma coluna `Status` registra `ok` para códigos concluídos, que não são processados novamente, e `erro` para códigos que continuam pendentes para uma execução futura.

## Tecnologias e decisões

- **.NET 9 / C# e Windows Forms:** hospedam a aplicação e a janela do navegador.
- **CefSharp/Chromium:** permite navegação real e interação programática com o DOM/JavaScript. Foi escolhido porque o portal depende do comportamento do navegador e de conteúdo dinâmico.
- **DdddOcr.Net:** executa OCR local, sem enviar o CAPTCHA a serviços externos.
- **OpenCvSharp:** gera versões em escala de cinza, contraste, limiarização e recortes para aumentar a diversidade das previsões.
- **ClosedXML:** lê e atualiza a planilha de entrada (códigos e coluna `Status`) sem exigir Microsoft Excel instalado.
- **xUnit:** valida as regras que não dependem do portal ou do navegador.

A navegação, captura do CAPTCHA, OCR, extração do resultado, configuração, logs e controle foram separados para reduzir o acoplamento e facilitar testes e manutenção.

## Pré-requisitos

- Windows 10 ou 11 de 64 bits;
- SDK do [.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0);
- acesso à internet para abrir o portal dos Correios e restaurar os pacotes NuGet;
- execução em arquitetura x64.

Não é necessário instalar Chrome, Python, Excel ou um mecanismo OCR externo. As dependências são restauradas pelo NuGet.

## Configuração

As opções ficam em `Automacao_Correios/config/.env`, uma por linha no formato `VARIAVEL=VALOR`:

```dotenv
URL_CORREIOS=https://rastreamento.correios.com.br/app/index.php
MAX_TENTATIVAS_TRANSACAO=5
TIMEOUT_EVENTOS_SEGUNDOS=8
MANTER_NAVEGADOR_ABERTO=false
```

| Variável | Finalidade |
|---|---|
| `URL_CORREIOS` | Endereço inicial do portal de rastreamento. |
| `MAX_TENTATIVAS_TRANSACAO` | Quantidade máxima de tentativas para erros transitórios. |
| `TIMEOUT_EVENTOS_SEGUNDOS` | Tempo máximo para o resultado aparecer após a consulta. |
| `MANTER_NAVEGADOR_ABERTO` | Quando `true`, mantém a janela aberta para inspeção após o processamento. |

## Entrada e controle

Sem argumentos, a aplicação lê e atualiza `Automacao_Correios/input/transacoes.xlsx`. A primeira planilha deve ter o cabeçalho `Código de rastreamento` na célula A1 e um código por linha na coluna A; a aplicação cria/mantém sozinha a coluna B (`Status`) na mesma planilha — não existe mais um arquivo de controle separado.

Também é possível informar parâmetros diretamente:

```powershell
dotnet run --project .\Automacao_Correios -- --codigo AP612167199BR
```

O argumento `--codigo` pode ser repetido:

```powershell
dotnet run --project .\Automacao_Correios -- --codigo AP612167199BR --codigo NN437873753BR
```

Nesse caso o controle (`Status`) é mantido em `Automacao_Correios/input/transacoes.xlsx` mesmo assim, adicionando os códigos informados caso ainda não existam na planilha.

Para usar outra planilha:

```powershell
dotnet run --project .\Automacao_Correios -- --arquivo C:\dados\transacoes.xlsx
```

Nesse caso o controle é feito diretamente no arquivo informado em `--arquivo`, na mesma coluna `Status`.

`--codigo` e `--arquivo` são mutuamente exclusivos. O formato SRO aceito é `AA123456789BR`.

## Executar

Na raiz do repositório:

```powershell
dotnet restore .\Automacao_Correios.sln
dotnet build .\Automacao_Correios.sln -c Release
dotnet run --project .\Automacao_Correios -c Release
```

Para gerar uma distribuição x64:

```powershell
dotnet publish .\Automacao_Correios\Automacao_Correios.csproj -c Release -r win-x64 --self-contained false -o .\publish
```

## Fluxo da automação

1. Carrega a configuração e cria um log exclusivo da execução.
2. Lê os códigos da linha de comando ou da coluna `Código de rastreamento` da planilha.
3. Mescla a planilha de entrada distribuída com a persistente (quando usa o caminho padrão) e ignora somente registros com status `ok` na coluna `Status`.
4. Valida o formato do código e abre o portal no Chromium.
5. Aguarda o formulário e a imagem do CAPTCHA estarem realmente disponíveis.
6. Captura os pixels originais do CAPTCHA pelo canvas; se isso falhar, usa screenshot da região do elemento.
7. Gera variantes da imagem e consulta os modelos Beta e Old do DdddOcr.Net.
8. Normaliza as previsões e seleciona o candidato por votos, concordância entre previsões e distância de Levenshtein.
9. Preenche o CAPTCHA, aguarda um intervalo aleatório de 0 a 2 segundos e clica em `Consultar`.
10. Detecta mensagens de erro ou aguarda os eventos. Se existir `Mais informações`, abre o histórico antes de extrair os dados.
11. Converte todos os elementos `li.step` para o modelo JSON e salva o resultado.
12. Atualiza o controle como `ok` ou `erro` e segue automaticamente para o próximo código.

## Saídas e controle

As pastas são criadas no diretório persistente do projeto:

- `Resultados/`: um JSON UTF-8 por código;
- `Captchas/`: imagem original, variantes e diagnóstico do OCR;
- `logs/`: um arquivo por execução no formato `execucao_dd-MM-yyyy_HH-mm.log`;
- `input/transacoes.xlsx`: código na coluna A (`Código de rastreamento`) e status na coluna B (`Status`), atualizado pela própria aplicação.

O status `ok` impede novo processamento. Status vazio ou `erro` torna o código elegível para uma nova execução. Ao mesclar a planilha distribuída com a persistente, `ok` tem prioridade sobre `erro`, evitando regressão de um item já concluído.

Um arquivo versionado de referência está em `exemplos/resultado-exemplo.json`.

## Tratamento de falhas

Falhas permanentes encerram imediatamente a transação e registram `erro`:

- código fora do formato esperado;
- objeto não encontrado;
- código rejeitado pelo portal como inválido.

Falhas transitórias respeitam `MAX_TENTATIVAS_TRANSACAO`. Antes da nova tentativa a página é recarregada, produzindo outro CAPTCHA:

- OCR sem previsão válida ou CAPTCHA recusado;
- timeout de formulário ou eventos;
- indisponibilidade temporária do portal;
- falha de navegação;
- seletor ou elemento esperado ausente.

Cada tentativa e seu resultado ficam registrados no log. Ao encerrar, o log também apresenta tentativas de OCR, consultas concluídas e a proporção de conclusão por tentativa. Essa proporção é operacional e não deve ser confundida com acurácia do OCR, que exige um conjunto de imagens com respostas conhecidas.

## Testes

Execute:

```powershell
dotnet test .\Automacao_Correios.sln -c Release
```

Os testes cobrem validação e normalização de códigos, leitura do `.env`, transformação de eventos, normalização/distância usadas pelo OCR e as regras de controle (coluna `Status`) da planilha de entrada. A integração real com o portal e o reconhecimento de CAPTCHAs variáveis exigem teste de execução, pois dependem de conteúdo externo.

## Limitações conhecidas

- O OCR é probabilístico. O projeto usa dois modelos e múltiplas variantes, mas não existe garantia de 100% de acerto; o mecanismo de tentativas é parte necessária da estratégia.
- Não é informada uma taxa de acerto sem uma amostra rotulada e representativa. Os diagnósticos em `Captchas/` permitem montar esse conjunto e medir acurácia sem inventar uma porcentagem.
- Seletores e mensagens do portal podem mudar. Ausência dos elementos esperados gera erro registrado em vez de produzir um JSON parcial silenciosamente.
- A aplicação depende da disponibilidade e do comportamento do site dos Correios.
- Datas são gravadas sem fuso (`yyyy-MM-ddTHH:mm:ss`) porque a página apresenta horário local sem offset explícito.

## Melhorias futuras

- manter um corpus anonimizado de CAPTCHAs rotulados e calcular taxa de acerto por versão;
- testes de integração com páginas HTML armazenadas localmente;
- injeção de dependências para substituir navegador, OCR e persistência em testes;
- interface para editar a fila e acompanhar métricas;
- execução em serviço ou API, caso o ambiente do portal permita esse modo.
