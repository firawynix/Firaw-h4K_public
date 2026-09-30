# Firaw h4K

Mascotes cibernéticos para o desktop Windows: dois besouros exploram a tela, criam painéis fictícios e efeitos visuais. O modo normal não bloqueia os controles. O modo desespero é opcional, exige aceite dentro do app e pode ser encerrado com **Ctrl+Alt+F12**; Ctrl+Alt+Del continua disponível. Evite o modo se você tem sensibilidade a flashes.

- [Site e download oficial](https://h4k.firawynix.com.br/)
- [Firaw Center](https://jogos.firawynix.com.br/games/firaw-h4k)

O projeto usa .NET/WPF. Para compilar o app: `dotnet build FirawH4K.csproj -c Release`. Os scripts de empacotamento estão em `packaging/` e `tools/`; a assinatura dos instaladores requer um certificado válido na conta do publicador. Binários e chaves privadas não fazem parte deste repositório.
