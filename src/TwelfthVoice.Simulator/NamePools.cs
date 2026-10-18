namespace TwelfthVoice.Simulator;

/// <summary>
/// Name pools used to invent players. Names are combined at random so squads are fictional;
/// no real club or player data is used.
/// </summary>
internal static class NamePools
{
    public sealed record Nation(string Name, string[] First, string[] Last);

    public static readonly Nation[] Nations =
    {
        new("England",
            new[] { "Callum", "Jack", "Reece", "Owen", "Harvey", "Tom", "Lewis", "Ryan", "Alfie", "Kieran" },
            new[] { "Ashworth", "Pembridge", "Holloway", "Thackeray", "Brindley", "Marsh", "Cartwright", "Fenwick", "Larkham", "Oakes" }),
        new("Spain",
            new[] { "Álvaro", "Iker", "Dani", "Rubén", "Pablo", "Unai" },
            new[] { "Ortega", "Valcárcel", "Medina", "Soler", "Arribas", "Peñalver" }),
        new("France",
            new[] { "Théo", "Mathis", "Yanis", "Bastien", "Lucas", "Enzo" },
            new[] { "Lefèvre", "Durand", "Moreau", "Garnier", "Rocheteau", "Vasseur" }),
        new("Brazil",
            new[] { "Thiago", "Rafael", "Caio", "Matheus", "Vinícius", "Gabriel" },
            new[] { "Andrade", "Moraes", "Teixeira", "Barros", "Quintela", "Pacheco" }),
        new("Nigeria",
            new[] { "Chidi", "Emeka", "Tunde", "Femi", "Uche" },
            new[] { "Okonkwo", "Adeyemi", "Balogun", "Ezeani", "Olatunji" }),
        new("Japan",
            new[] { "Haruto", "Ren", "Sota", "Daiki", "Kaito" },
            new[] { "Takeda", "Morioka", "Kanda", "Fujii", "Shimizu" }),
        new("Germany",
            new[] { "Lukas", "Jonas", "Felix", "Nico", "Moritz" },
            new[] { "Kessler", "Vogt", "Ahlmann", "Dietrich", "Lindner" }),
        new("Netherlands",
            new[] { "Daan", "Sem", "Jesse", "Bram", "Thijs" },
            new[] { "Verhoef", "van Dalen", "Mulder", "Kuipers", "Hendriksen" }),
        new("Ghana",
            new[] { "Kwame", "Kofi", "Yaw", "Kwabena" },
            new[] { "Owusu", "Asante", "Boateng-Addo", "Darko" }),
        new("Argentina",
            new[] { "Facundo", "Tomás", "Santiago", "Joaquín" },
            new[] { "Ibarra", "Quiroga", "Bustos", "Arrieta" }),
        new("Norway",
            new[] { "Sindre", "Eirik", "Magnus", "Sander" },
            new[] { "Haugland", "Solberg", "Brekke", "Aasen" }),
        new("Morocco",
            new[] { "Yassine", "Anas", "Ilias", "Hamza" },
            new[] { "El Idrissi", "Benali", "Zerouali", "Amrani" }),
        new("South Korea",
            new[] { "Min-jun", "Ji-ho", "Seung-woo", "Do-yun" },
            new[] { "Seo", "Ryu", "Bae", "Yoon" }),
        new("Portugal",
            new[] { "Rúben", "Diogo", "Tiago", "Gonçalo" },
            new[] { "Ferreirinha", "Carvalhal", "Lobato", "Meireles" }),
        new("Ireland",
            new[] { "Cian", "Darragh", "Oisín", "Seán" },
            new[] { "Gallagher", "Kinsella", "Doherty", "Mulcahy" }),
    };
}
