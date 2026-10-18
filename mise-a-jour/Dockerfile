# Service de mise à jour de Gestion MEP : facultatif, activé par l'équipe informatique (profil Compose « mise-a-jour »).
# Il attend une demande de l'application (fichier dans le volume partagé), télécharge l'image de la version demandée,
# l'écrit dans MEP_IMAGE du fichier .env, puis recrée le conteneur de l'application.
FROM mirror.gcr.io/library/docker:28-cli
COPY mise-a-jour.sh /usr/local/bin/mise-a-jour
RUN chmod 755 /usr/local/bin/mise-a-jour
ENTRYPOINT ["/usr/local/bin/mise-a-jour"]
