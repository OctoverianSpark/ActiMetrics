# ActiMetrics — Guía de usuario

ActiMetrics es una aplicación de escritorio para Windows que registra tu actividad laboral durante la jornada y la sincroniza con el servidor de la empresa. Viene instalada en tu equipo y arranca automáticamente al iniciar sesión.

Al usarla por primera vez se pedirá tu **correo corporativo** para vincular el equipo a tu perfil.

---

## Interfaz — icono en la bandeja del sistema

ActiMetrics vive en la barra de tareas (esquina inferior derecha). Al hacer **clic derecho** sobre el icono aparece el menú de estados.

El **tooltip** del icono muestra tu estado actual y el tiempo activo acumulado en el día:

```
[Working] Activo: 05h 32m 18s
```

---

## Estados de trabajo

Selecciona el estado que corresponde a lo que estás haciendo en cada momento.

| Estado | Categoría | Cuándo usarlo |
|---|---|---|
| **Trabajando** | Activo | Tarea normal en el equipo |
| **Horas Extras** | Activo | Continuar trabajando después del fin de jornada |
| **Break** | Neutral | Pausa corta |
| **Baño** | Neutral | Salida breve al baño |
| **Almuerzo** | Neutral | Hora de almuerzo |

> Los estados **Idle** y **Fuera de línea** los gestiona la aplicación automáticamente y no aparecen en el menú.

---

## Detección automática de inactividad

Si el sistema detecta que no hay actividad de ratón ni teclado, el estado cambia automáticamente a **Idle**. Al volver a usar el equipo, el estado vuelve a **Trabajando** sin que tengas que hacer nada.

---

## Notificaciones de jornada

La aplicación muestra notificaciones emergentes en los momentos clave del día:

| Momento | Aviso |
|---|---|
| 5 min antes del almuerzo | Recordatorio de inicio de almuerzo |
| Inicio del almuerzo | Hora de regreso |
| 5 min antes del fin de jornada | Aviso de cierre próximo |
| Fin de jornada | Opción de marcar Horas Extras |

Si seleccionas **Horas Extras** desde el menú o desde la notificación, la aplicación cancela el apagado programado por fin de jornada y continúa registrando.

---

## Crear un ticket de soporte

Desde el menú de la bandeja selecciona **Crear Ticket**, rellena la categoría y descripción, y el ticket se envía directamente al sistema de helpdesk.

---

## Actualizaciones

Las actualizaciones son automáticas. Cuando hay una nueva versión disponible, la aplicación se cierra sola, instala la actualización y vuelve a arrancar. No necesitas hacer nada.

---

## Preguntas frecuentes

**¿Puedo cerrar la aplicación?**
La aplicación se reinicia automáticamente si se cierra. Si necesitas detenerla por completo, contacta a TI.

**¿Qué datos se recopilan?**
Se registran los estados de trabajo, el tiempo por aplicación activa y capturas de pantalla periódicas. Todo se sincroniza con el servidor de la empresa.

**¿Qué pasa si no tengo conexión a internet?**
Los datos se guardan localmente y se sincronizan en cuanto se recupera la conexión.

**El icono no aparece en la bandeja**
Reinicia el equipo. Si el problema persiste, contacta a TI.
