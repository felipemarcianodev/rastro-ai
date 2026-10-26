workspace "fixture: AlertJob.ExecuteAsync" "Gerado pelo Rastro AI a partir do fluxo levantado pelo compilador (solution Fixture.slnx, commit 0123456)." {

    !identifiers hierarchical

    model {
        pessoa1 = person "Operador" "Acompanha os alertas enviados."
        sistema = softwareSystem "fixture" "Fluxo AlertJob.ExecuteAsync." {
            cont1 = container "Alerts" "" ".NET" {
                comp1 = component "AlertJob" "Job agendado." "C#"
                comp2 = component "SystemClock" "" "C#"
                comp3 = component "PlainFormatter" "" "C#"
                comp4 = component "TelegramAlertSender" "" "C#"
            }
        }
        ext1 = softwareSystem "Serviço HTTP externo" "Sistema externo chamado pelo fluxo." "Externo"

        pessoa1 -> sistema.cont1 "Usa"
        sistema.cont1.comp1 -> sistema.cont1.comp2 "Now"
        sistema.cont1.comp1 -> sistema.cont1.comp3 "Format"
        sistema.cont1.comp1 -> sistema.cont1.comp4 "SendAsync"
        sistema.cont1.comp4 -> ext1 "PostAsync"
    }

    views {
        systemContext sistema "C1-contexto" "C1: contexto do fluxo." {
            include *
            autolayout lr
        }
        container sistema "C2-containers" "C2: projetos e banco usados pelo fluxo." {
            include *
            autolayout lr
        }
        component sistema.cont1 "C3-cont1" "C3: componentes de Alerts." {
            include *
            autolayout lr
        }
        dynamic sistema.cont1 "C4-fluxo" "C4: ordem das chamadas a partir de AlertJob.ExecuteAsync." {
            sistema.cont1.comp1 -> sistema.cont1.comp2 "Now"
            sistema.cont1.comp1 -> sistema.cont1.comp3 "Format"
            sistema.cont1.comp1 -> sistema.cont1.comp4 "SendAsync"
            sistema.cont1.comp4 -> ext1 "PostAsync"
            autolayout lr
        }

        styles {
            element "Person" {
                shape person
            }
            element "Database" {
                shape cylinder
            }
            element "Externo" {
                background #8a8f98
                color #ffffff
            }
        }
    }
}
